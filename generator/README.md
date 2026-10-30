# Generator danych (emulator czujnika)

Narzędzie konsolowe w Pythonie (3.10+, tylko biblioteka standardowa), które wysyła wyniki pomiarów do API
aplikacji przez `POST /api/measurements` z kluczem czujnika w nagłówku `X-API-Key`. Nie zapisuje niczego
bezpośrednio do bazy danych.

## Szybki start

1. Zarejestruj czujnik w panelu administratora i skopiuj jego klucz API (wyświetlany tylko raz).
2. Ustaw adres aplikacji i klucz, a następnie uruchom generator:

```bash
export ZAI_API_URL=https://zai.example.com
export ZAI_API_KEY=zai_...
python3 generator/zai_sensor.py backfill --count 48 --step 1h --pattern sine --min 2 --max 14
python3 generator/zai_sensor.py live --interval 10s --pattern open-meteo --lat 52.23 --lon 21.01
```

## Tryby pracy

| Tryb | Opis | Parametry |
| --- | --- | --- |
| `backfill` | Seria wyników ze znacznikami czasu z przeszłości (np. dane przykładowe) | `--count` (domyślnie 48), `--step` (1h), `--end` (teraz, zaokrąglone do `--step`) |
| `live` | Bieżące wyniki wysyłane w stałych odstępach czasu | `--interval` (10s), `--count` (bez limitu, Ctrl+C kończy) |
| `once` | Pojedynczy wynik, np. do pokazania odrzucenia wartości spoza przedziału | `--value`, `--timestamp` (domyślnie czas serwera) |

Czasy trwania podaje się z jednostką: `500ms`, `10s`, `5m`, `1h`, `1d`. Daty – w formacie ISO 8601 ze strefą
czasową, np. `2026-11-02T08:00:00Z`.

## Parametry wspólne

| Parametr | Domyślnie | Opis |
| --- | --- | --- |
| `--api-url` | `$ZAI_API_URL` lub `http://localhost:8080` | Adres aplikacji |
| `--api-key` | `$ZAI_API_KEY` | Klucz API czujnika (nie jest zapisany w kodzie) |
| `--timeout` | `10` | Limit czasu żądania w sekundach |
| `--retries` | `3` | Liczba ponowień przy błędach sieci i odpowiedziach 5xx |
| `--dry-run` | – | Tylko wypisuje wygenerowane wartości, nic nie wysyła |
| `-v`, `--verbose` | – | Wypisuje pełne odpowiedzi API |

Parametry globalne podaje się przed nazwą trybu, np. `zai_sensor.py --dry-run backfill --count 5`.

## Sposób generowania wartości (`--pattern`)

| Wzorzec | Opis | Parametry |
| --- | --- | --- |
| `sine` (domyślny) | Przebieg sinusoidalny między `--min` a `--max` | `--period` (24h) |
| `uniform` | Wartości losowe z rozkładu jednostajnego | `--min`, `--max` |
| `random-walk` | Błądzenie losowe ograniczone do przedziału | `--min`, `--max`, `--step-size` (0.5) |
| `constant` | Stała wartość (domyślny wzorzec w trybie `once`) | `--value` |
| `open-meteo` | Rzeczywista temperatura (lub inna zmienna) z serwisu [Open-Meteo](https://open-meteo.com/) | `--lat`, `--lon`, `--variable` (`temperature_2m`) |

Do każdego wzorca można dodać szum gaussowski: `--noise 0.5`.

## Komunikaty i kody wyjścia

| Odpowiedź API | Komunikat | Kod wyjścia |
| --- | --- | --- |
| 201 | `✔ <czas> <wartość> → /api/measurements/<id>` | 0 |
| 422 | `✘ … ODRZUCONO: Wartość 60 jest spoza dopuszczalnego przedziału serii [-40, 45].` | 1 |
| 401 | `✘ Odmowa przyjęcia wyników (401): klucz API jest nieprawidłowy albo czujnik został wyrejestrowany.` | 2 |
| brak połączenia | `✘ … błąd połączenia z API` (po ponowieniach) | 1 |
| brak klucza | `Błąd: podaj klucz API czujnika` | 3 |

## Dane przykładowe (`seed_demo.py`)

Skrypt loguje się jako administrator, tworzy przez API serie „Warszawa”, „Kraków”, „Gdańsk” i „Wrocław”
(°C, przedział −40…45), rejestruje dla każdej czujnik i jego kluczem wysyła historię temperatury z ostatnich
7 dni co godzinę (168 wyników na serię). Serie, które mają już co najmniej 15 wyników, są pomijane.

```bash
ZAI_API_URL=https://zai.example.com ZAI_ADMIN_USER=admin ZAI_ADMIN_PASS='…' python3 generator/seed_demo.py
python3 generator/seed_demo.py --pattern sine --days 2   # bez dostępu do Open-Meteo
```
