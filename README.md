# ZAI IoT – serie pomiarowe

[![CI](https://github.com/Pyruus/zai-iot-26z/actions/workflows/ci.yml/badge.svg)](https://github.com/Pyruus/zai-iot-26z/actions/workflows/ci.yml)

Aplikacja do zbierania i prezentacji wyników pomiarów z czujników IoT (projekt ZAI 26Z).

| Katalog | Zawartość |
| --- | --- |
| `backend/` | REST API – ASP.NET Core (.NET 10), EF Core, PostgreSQL/TimescaleDB |
| `frontend/` | SPA – Angular |
| `generator/` | Emulator czujnika (Python) |
| `docs/` | Kontrakt API, dokumentacja, diagram ERD |
| `tests/` | Testy automatyczne prowadzącego (`zai-tests.mjs`) |

## Uruchomienie lokalne

```bash
cp .env.example .env
docker compose up -d --build
ZAI_ADMIN_PASSWORD='…' docker compose exec -e ZAI_ADMIN_PASSWORD api dotnet Zai.Api.dll create-admin --username admin
ZAI_ADMIN_PASS='…' python3 generator/seed_demo.py
```

Aplikacja działa pod `http://localhost:8080`. Opis generatora: [generator/README.md](generator/README.md), wdrożenie: [deploy/README.md](deploy/README.md).
