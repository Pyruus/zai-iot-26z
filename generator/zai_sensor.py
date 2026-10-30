#!/usr/bin/env python3
"""Emulator czujnika IoT dla API projektu ZAI 26Z (tylko biblioteka standardowa)."""

from __future__ import annotations

import argparse
import http.client
import json
import math
import os
import random
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone

DEFAULT_API_URL = "http://localhost:8080"
OPEN_METEO_URL = "https://api.open-meteo.com/v1/forecast"
EXIT_OK, EXIT_PARTIAL, EXIT_AUTH, EXIT_USAGE = 0, 1, 2, 3


def parse_duration(text: str) -> timedelta:
    match = re.fullmatch(r"(\d+(?:\.\d+)?)\s*(ms|s|m|h|d)", text.strip())
    if not match:
        raise argparse.ArgumentTypeError(f"niepoprawny czas trwania: {text!r} (przykłady: 500ms, 10s, 5m, 1h, 1d)")
    amount, unit = float(match.group(1)), match.group(2)
    return {
        "ms": timedelta(milliseconds=amount),
        "s": timedelta(seconds=amount),
        "m": timedelta(minutes=amount),
        "h": timedelta(hours=amount),
        "d": timedelta(days=amount),
    }[unit]


def parse_timestamp(text: str) -> datetime:
    if text == "now":
        return now_utc()
    try:
        value = datetime.fromisoformat(text.replace("Z", "+00:00"))
    except ValueError as error:
        raise argparse.ArgumentTypeError(f"niepoprawna data ISO 8601: {text!r}") from error
    if value.tzinfo is None:
        raise argparse.ArgumentTypeError(f"data musi zawierać strefę czasową, np. {text}Z")
    return value.astimezone(timezone.utc)


def now_utc() -> datetime:
    return datetime.now(timezone.utc).replace(microsecond=0)


def iso(ts: datetime) -> str:
    return ts.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


class ValueSource:
    def value_at(self, ts: datetime) -> float:
        raise NotImplementedError


@dataclass
class Uniform(ValueSource):
    low: float
    high: float

    def value_at(self, ts: datetime) -> float:
        return random.uniform(self.low, self.high)


@dataclass
class RandomWalk(ValueSource):
    low: float
    high: float
    step: float
    current: float | None = None

    def value_at(self, ts: datetime) -> float:
        if self.current is None:
            self.current = (self.low + self.high) / 2
        self.current = min(self.high, max(self.low, self.current + random.uniform(-self.step, self.step)))
        return self.current


@dataclass
class Sine(ValueSource):
    low: float
    high: float
    period: timedelta

    def value_at(self, ts: datetime) -> float:
        middle, amplitude = (self.low + self.high) / 2, (self.high - self.low) / 2
        phase = 2 * math.pi * ts.timestamp() / self.period.total_seconds()
        return middle + amplitude * math.sin(phase)


@dataclass
class Constant(ValueSource):
    value: float

    def value_at(self, ts: datetime) -> float:
        return self.value


@dataclass
class OpenMeteo(ValueSource):
    latitude: float
    longitude: float
    variable: str
    timeout: float
    _hourly: dict[datetime, float] = field(default_factory=dict)
    _current: tuple[float, float] | None = None

    def value_at(self, ts: datetime) -> float:
        if now_utc() - ts < timedelta(minutes=30):
            return self._current_value()
        hour = ts.replace(minute=0, second=0, microsecond=0)
        if hour not in self._hourly:
            self._load_history(hour)
        if hour not in self._hourly:
            raise RuntimeError(f"Open-Meteo nie zwróciło wartości {self.variable} dla {iso(hour)}")
        return self._hourly[hour]

    def _load_history(self, oldest: datetime) -> None:
        days = min(92, max(1, (now_utc() - oldest).days + 1))
        data = self._fetch({"hourly": self.variable, "past_days": days, "forecast_days": 1})
        for stamp, value in zip(data["hourly"]["time"], data["hourly"][self.variable]):
            if value is not None:
                self._hourly[datetime.fromisoformat(stamp).replace(tzinfo=timezone.utc)] = float(value)

    def _current_value(self) -> float:
        if self._current is None or time.monotonic() - self._current[0] > 60:
            data = self._fetch({"current": self.variable})
            self._current = (time.monotonic(), float(data["current"][self.variable]))
        return self._current[1]

    def _fetch(self, params: dict, attempts: int = 3) -> dict:
        query = urllib.parse.urlencode({
            "latitude": self.latitude, "longitude": self.longitude, "timezone": "UTC", **params,
        })
        for attempt in range(attempts):
            try:
                with urllib.request.urlopen(f"{OPEN_METEO_URL}?{query}", timeout=self.timeout) as response:
                    return json.load(response)
            except (OSError, http.client.HTTPException, ValueError) as error:
                if attempt == attempts - 1:
                    raise RuntimeError(f"nie udało się pobrać danych z Open-Meteo: {error}") from error
                time.sleep(2 ** attempt)
        raise AssertionError("unreachable")


@dataclass
class Noisy(ValueSource):
    inner: ValueSource
    sigma: float

    def value_at(self, ts: datetime) -> float:
        return self.inner.value_at(ts) + random.gauss(0, self.sigma)


def build_source(args: argparse.Namespace) -> ValueSource:
    if args.pattern != "constant" and args.min >= args.max:
        raise SystemExit("Błąd: --min musi być mniejsze od --max.")
    source: ValueSource = {
        "uniform": lambda: Uniform(args.min, args.max),
        "random-walk": lambda: RandomWalk(args.min, args.max, args.step_size),
        "sine": lambda: Sine(args.min, args.max, args.period),
        "constant": lambda: Constant(args.value),
        "open-meteo": lambda: OpenMeteo(args.lat, args.lon, args.variable, args.timeout),
    }[args.pattern]()
    return Noisy(source, args.noise) if args.noise > 0 else source


@dataclass
class Stats:
    sent: int = 0
    rejected: int = 0
    failed: int = 0

    def exit_code(self) -> int:
        return EXIT_OK if self.rejected == 0 and self.failed == 0 else EXIT_PARTIAL


class AuthError(Exception):
    pass


class SensorClient:
    def __init__(self, api_url: str, api_key: str | None, timeout: float, retries: int, dry_run: bool,
                 verbose: bool):
        self.endpoint = api_url.rstrip("/") + "/api/measurements"
        self.api_key = api_key
        self.timeout = timeout
        self.retries = retries
        self.dry_run = dry_run
        self.verbose = verbose
        self.stats = Stats()

    def send(self, value: float, ts: datetime | None) -> None:
        body: dict = {"value": round(value, 2)}
        if ts is not None:
            body["timestamp"] = iso(ts)
        label = f"{body.get('timestamp', 'teraz'):>20}  {body['value']:>9}"

        if self.dry_run:
            print(f"· {label}  (tryb próbny, nie wysłano)")
            self.stats.sent += 1
            return

        for attempt in range(self.retries + 1):
            try:
                status, headers, payload = self._post(body)
            except (OSError, http.client.HTTPException) as error:
                if attempt < self.retries:
                    time.sleep(2 ** attempt)
                    continue
                print(f"✘ {label}  błąd połączenia z API: {getattr(error, 'reason', error)}")
                self.stats.failed += 1
                return

            if status >= 500 and attempt < self.retries:
                time.sleep(2 ** attempt)
                continue
            break

        if status == 201:
            self.stats.sent += 1
            print(f"✔ {label}  → {headers.get('Location', '')}")
        elif status == 401:
            raise AuthError("klucz API jest nieprawidłowy albo czujnik został wyrejestrowany.")
        elif status == 422:
            self.stats.rejected += 1
            print(f"✘ {label}  ODRZUCONO: {describe(payload)}")
        else:
            self.stats.failed += 1
            print(f"✘ {label}  błąd HTTP {status}: {describe(payload)}")

        if self.verbose and payload:
            print(f"    {json.dumps(payload, ensure_ascii=False)}")

    def _post(self, body: dict) -> tuple[int, dict, dict | None]:
        request = urllib.request.Request(
            self.endpoint,
            data=json.dumps(body).encode(),
            method="POST",
            headers={"Content-Type": "application/json", "Accept": "application/json",
                     "X-API-Key": self.api_key or ""},
        )
        try:
            with urllib.request.urlopen(request, timeout=self.timeout) as response:
                return response.status, dict(response.headers), read_json(response.read())
        except urllib.error.HTTPError as error:
            return error.code, dict(error.headers), read_json(error.read())


def read_json(raw: bytes) -> dict | None:
    try:
        return json.loads(raw) if raw else None
    except ValueError:
        return None


def describe(problem: dict | None) -> str:
    if not problem:
        return ""
    return problem.get("detail") or problem.get("title") or ""


def run_backfill(client: SensorClient, source: ValueSource, count: int, step: timedelta,
                 end: datetime | None = None) -> None:
    if end is None:
        seconds = step.total_seconds()
        end = datetime.fromtimestamp(now_utc().timestamp() // seconds * seconds, timezone.utc)
    if end > now_utc():
        raise SystemExit("Błąd: --end nie może być w przyszłości.")
    start = end - step * (count - 1)
    print(f"Wysyłanie {count} wyników z przedziału {iso(start)} – {iso(end)} co {step}.")
    for i in range(count):
        ts = start + step * i
        client.send(source.value_at(ts), ts)


def run_live(client: SensorClient, source: ValueSource, interval: timedelta, count: int | None) -> None:
    limit = "bez limitu" if count is None else f"liczba wyników: {count}"
    print(f"Tryb ciągły: wynik co {interval} ({limit}). Ctrl+C kończy pracę.")
    sent = 0
    while count is None or sent < count:
        started = time.monotonic()
        ts = now_utc()
        client.send(source.value_at(ts), ts)
        sent += 1
        if count is None or sent < count:
            time.sleep(max(0.0, interval.total_seconds() - (time.monotonic() - started)))


def build_parser() -> argparse.ArgumentParser:
    common = argparse.ArgumentParser(add_help=False)
    values = common.add_argument_group("generowanie wartości")
    values.add_argument("--pattern", choices=["uniform", "random-walk", "sine", "constant", "open-meteo"],
                        default="sine", help="sposób generowania wartości (domyślnie: sine)")
    values.add_argument("--min", type=float, default=5.0, help="dolna granica wartości (domyślnie: 5)")
    values.add_argument("--max", type=float, default=20.0, help="górna granica wartości (domyślnie: 20)")
    values.add_argument("--period", type=parse_duration, default=timedelta(hours=24),
                        help="okres przebiegu sinusoidalnego (domyślnie: 24h)")
    values.add_argument("--step-size", type=float, default=0.5,
                        help="maksymalna zmiana w kroku błądzenia losowego (domyślnie: 0.5)")
    values.add_argument("--noise", type=float, default=0.0,
                        help="odchylenie standardowe szumu gaussowskiego (domyślnie: 0)")
    values.add_argument("--value", type=float, default=0.0, help="wartość dla --pattern constant")
    values.add_argument("--lat", type=float, default=52.23, help="szerokość geograficzna dla open-meteo")
    values.add_argument("--lon", type=float, default=21.01, help="długość geograficzna dla open-meteo")
    values.add_argument("--variable", default="temperature_2m",
                        help="zmienna Open-Meteo (domyślnie: temperature_2m)")

    parser = argparse.ArgumentParser(
        description="Emulator czujnika: wysyła wyniki pomiarów do API (POST /api/measurements).",
        epilog="Klucz API można podać w --api-key albo w zmiennej środowiskowej ZAI_API_KEY.",
    )
    parser.add_argument("--api-url", default=os.environ.get("ZAI_API_URL", DEFAULT_API_URL),
                        help=f"adres aplikacji (domyślnie: $ZAI_API_URL lub {DEFAULT_API_URL})")
    parser.add_argument("--api-key", default=os.environ.get("ZAI_API_KEY"),
                        help="klucz API czujnika (domyślnie: $ZAI_API_KEY)")
    parser.add_argument("--timeout", type=float, default=10.0, help="limit czasu żądania w sekundach")
    parser.add_argument("--retries", type=int, default=3, help="liczba ponowień przy błędach sieci/5xx")
    parser.add_argument("--dry-run", action="store_true", help="tylko wypisz wartości, nic nie wysyłaj")
    parser.add_argument("-v", "--verbose", action="store_true", help="wypisuj pełne odpowiedzi API")

    modes = parser.add_subparsers(dest="mode", required=True, metavar="{backfill,live,once}")
    backfill = modes.add_parser("backfill", parents=[common], help="seria wyników ze znacznikami z przeszłości")
    backfill.add_argument("--count", type=int, default=48, help="liczba wyników (domyślnie: 48)")
    backfill.add_argument("--step", type=parse_duration, default=timedelta(hours=1),
                          help="odstęp między znacznikami czasu (domyślnie: 1h)")
    backfill.add_argument("--end", type=parse_timestamp, default=None,
                          help="znacznik ostatniego wyniku, ISO 8601 lub 'now' "
                               "(domyślnie: teraz, zaokrąglone w dół do wielokrotności --step)")

    live = modes.add_parser("live", parents=[common], help="bieżące wyniki w stałych odstępach czasu")
    live.add_argument("--interval", type=parse_duration, default=timedelta(seconds=10),
                      help="odstęp między wynikami (domyślnie: 10s)")
    live.add_argument("--count", type=int, default=None, help="liczba wyników (domyślnie: bez limitu)")

    once = modes.add_parser("once", parents=[common], help="pojedynczy wynik")
    once.add_argument("--timestamp", type=parse_timestamp, default=None,
                      help="znacznik czasu ISO 8601 (domyślnie: czas serwera)")
    return parser


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if not args.api_key and not args.dry_run:
        print("Błąd: podaj klucz API czujnika (--api-key lub ZAI_API_KEY).", file=sys.stderr)
        return EXIT_USAGE
    if getattr(args, "count", None) is not None and args.count < 1:
        print("Błąd: --count musi być dodatnie.", file=sys.stderr)
        return EXIT_USAGE

    if args.mode == "once" and args.pattern == "sine":
        args.pattern = "constant"
    source = build_source(args)
    client = SensorClient(args.api_url, args.api_key, args.timeout, args.retries, args.dry_run, args.verbose)

    try:
        if args.mode == "backfill":
            run_backfill(client, source, args.count, args.step, args.end)
        elif args.mode == "live":
            run_live(client, source, args.interval, args.count)
        else:
            client.send(source.value_at(args.timestamp or now_utc()), args.timestamp)
    except AuthError as error:
        print(f"✘ Odmowa przyjęcia wyników (401): {error}", file=sys.stderr)
        print_summary(client.stats)
        return EXIT_AUTH
    except KeyboardInterrupt:
        print("\nPrzerwano.")
    except RuntimeError as error:
        print(f"✘ Błąd źródła danych: {error}", file=sys.stderr)
        print_summary(client.stats)
        return EXIT_PARTIAL

    print_summary(client.stats)
    return client.stats.exit_code()


def print_summary(stats: Stats) -> None:
    print(f"Podsumowanie: zapisane {stats.sent}, odrzucone {stats.rejected}, błędy {stats.failed}.")


if __name__ == "__main__":
    sys.exit(main())
