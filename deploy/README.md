# Wdrożenie produkcyjne

Produkcja działa na VPS z Dockerem. Ruch HTTPS obsługuje Caddy (automatyczny certyfikat Let's Encrypt
i przekierowanie http → https); API i baza danych nie są wystawione na zewnątrz. Obraz aplikacji buduje
GitHub Actions i publikuje w `ghcr.io/pyruus/zai-iot-26z`.

```
Internet ──443/80──▶ caddy ──8080──▶ api (ASP.NET Core + Angular) ──5432──▶ db (PostgreSQL + TimescaleDB)
```

| Plik | Opis |
| --- | --- |
| `docker-compose.yml` | Kontenery `caddy`, `api` i `db` z limitami pamięci (serwer 1 GB RAM) |
| `Caddyfile` | Reverse proxy z HTTPS dla domeny z `DOMAIN` |
| `.env.example` | Wzór konfiguracji: domena, hasło do bazy, wersja obrazu |
| `backup.sh` | Kopia bazy (`pg_dump`) przechowywana 14 dni |

## Uruchomienie

Wymagania: Docker z Compose, domena wskazująca na serwer, otwarte porty 80 i 443.

```bash
git clone https://github.com/Pyruus/zai-iot-26z.git /opt/zai
cd /opt/zai/deploy
cp .env.example .env            # uzupełnić DOMAIN i POSTGRES_PASSWORD
docker compose up -d
curl https://<domena>/api/health
```

Konto administratora:

```bash
read -rs ZAI_ADMIN_PASSWORD && export ZAI_ADMIN_PASSWORD
docker compose exec -e ZAI_ADMIN_PASSWORD api dotnet Zai.Api.dll create-admin --username admin
unset ZAI_ADMIN_PASSWORD
```

Dane przykładowe wprowadza generator przez API (`generator/seed_demo.py`, zob. `generator/README.md`).

## Aktualizacja

Każdy push na `main` publikuje obrazy `latest` i `sha-<commit>`. Ustawienie `API_TAG=sha-<commit>` w `.env`
przypina konkretną wersję.

```bash
docker compose pull api && docker compose up -d api && docker image prune -f
```

## Kopie zapasowe

```bash
( crontab -l 2>/dev/null; echo '30 3 * * * /opt/zai/deploy/backup.sh' ) | crontab -
docker compose exec -T db pg_restore -U zai -d zai --clean < backups/<plik>.dump   # odtworzenie
```
