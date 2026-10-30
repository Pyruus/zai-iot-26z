#!/usr/bin/env python3
"""Przygotowuje dane przykładowe (F11) wyłącznie przez API: serie, czujniki i historyczne wyniki."""

from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from datetime import timedelta

from zai_sensor import DEFAULT_API_URL, AuthError, OpenMeteo, SensorClient, Sine, Noisy, run_backfill

CITIES = [
    {"name": "Warszawa", "lat": 52.23, "lon": 21.01, "color": "#1f77b4", "icon": "circle"},
    {"name": "Kraków", "lat": 50.06, "lon": 19.94, "color": "#d62728", "icon": "triangle"},
    {"name": "Gdańsk", "lat": 54.35, "lon": 18.65, "color": "#2ca02c", "icon": "rect"},
    {"name": "Wrocław", "lat": 51.11, "lon": 17.03, "color": "#9467bd", "icon": "rectRot"},
]
MIN_VALUE, MAX_VALUE, UNIT = -40.0, 45.0, "°C"


class Api:
    def __init__(self, base_url: str, timeout: float):
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.token: str | None = None

    def call(self, method: str, path: str, body: dict | None = None) -> tuple[int, object]:
        headers = {"Accept": "application/json"}
        data = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            data = json.dumps(body).encode()
        if self.token:
            headers["Authorization"] = f"Bearer {self.token}"
        request = urllib.request.Request(self.base_url + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                raw = response.read()
                return response.status, json.loads(raw) if raw else None
        except urllib.error.HTTPError as error:
            raw = error.read()
            return error.code, json.loads(raw) if raw else None

    def expect(self, expected: int, method: str, path: str, body: dict | None = None) -> object:
        status, payload = self.call(method, path, body)
        if status != expected:
            detail = payload.get("detail") or payload.get("title") if isinstance(payload, dict) else payload
            raise SystemExit(f"Błąd: {method} {path} zwróciło {status}: {detail}")
        return payload


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-url", default=os.environ.get("ZAI_API_URL", DEFAULT_API_URL))
    parser.add_argument("--user", default=os.environ.get("ZAI_ADMIN_USER", "admin"))
    parser.add_argument("--password", default=os.environ.get("ZAI_ADMIN_PASS"),
                        help="hasło administratora (domyślnie: $ZAI_ADMIN_PASS)")
    parser.add_argument("--days", type=int, default=7, help="liczba dni historii (domyślnie: 7)")
    parser.add_argument("--step", type=int, default=1, help="odstęp między wynikami w godzinach (domyślnie: 1)")
    parser.add_argument("--pattern", choices=["open-meteo", "sine"], default="open-meteo",
                        help="źródło wartości: rzeczywiste dane Open-Meteo albo sinusoida z szumem")
    parser.add_argument("--min-points", type=int, default=15,
                        help="pomiń serię, jeśli ma już co najmniej tyle wyników (domyślnie: 15)")
    parser.add_argument("--timeout", type=float, default=15.0)
    args = parser.parse_args()

    if not args.password:
        print("Błąd: podaj hasło administratora (--password lub ZAI_ADMIN_PASS).", file=sys.stderr)
        return 3

    api = Api(args.api_url, args.timeout)
    api.token = api.expect(200, "POST", "/api/auth/login", {"username": args.user, "password": args.password})["accessToken"]
    existing = {s["name"]: s for s in api.expect(200, "GET", "/api/series")}
    count = args.days * 24 // args.step

    try:
        for city in CITIES:
            series = existing.get(city["name"])
            if series is None:
                series = api.expect(201, "POST", "/api/series", {
                    "name": city["name"], "minValue": MIN_VALUE, "maxValue": MAX_VALUE,
                    "color": city["color"], "icon": city["icon"], "unit": UNIT,
                })
                print(f"Utworzono serię „{city['name']}” (id {series['id']}).")
            else:
                points = api.expect(200, "GET", f"/api/measurements?series={series['id']}&limit=10000")
                if len(points) >= args.min_points:
                    print(f"Seria „{city['name']}” ma już {len(points)} wyników – pominięto.")
                    continue

            sensor = api.expect(201, "POST", "/api/sensors",
                                {"name": f"Stacja {city['name']}", "seriesId": series["id"]})
            print(f"Zarejestrowano czujnik „{sensor['name']}” (id {sensor['id']}).")

            source = (OpenMeteo(city["lat"], city["lon"], "temperature_2m", args.timeout)
                      if args.pattern == "open-meteo"
                      else Noisy(Sine(-2.0, 12.0, timedelta(hours=24)), 0.6))
            client = SensorClient(args.api_url, sensor["apiKey"], args.timeout, retries=3, dry_run=False,
                                  verbose=False)
            try:
                run_backfill(client, source, count, timedelta(hours=args.step))
            except RuntimeError as error:
                print(f"✘ {city['name']}: {error}", file=sys.stderr)
            print(f"„{city['name']}”: zapisane {client.stats.sent}, odrzucone {client.stats.rejected}, "
                  f"błędy {client.stats.failed}.\n")
    except AuthError as error:
        print(f"Błąd: {error}", file=sys.stderr)
        return 2
    finally:
        api.call("POST", "/api/auth/logout")

    return 0


if __name__ == "__main__":
    sys.exit(main())
