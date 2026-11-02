# Wdrożenie produkcyjne

Serwer: VPS z Ubuntu (1 GB RAM), publiczny adres IPv4, Docker Compose. Ruch HTTPS obsługuje Caddy
(automatyczny certyfikat Let's Encrypt i przekierowanie http → https), API i baza danych nie są wystawione
na zewnątrz. Obraz aplikacji buduje GitHub Actions i publikuje w `ghcr.io/pyruus/zai-iot-26z`.

```
Internet ──443/80──▶ caddy ──8080──▶ api (ASP.NET Core + Angular) ──5432──▶ db (PostgreSQL + TimescaleDB)
```

## 1. Domena

Darmowa subdomena w [DuckDNS](https://www.duckdns.org): logowanie przez GitHub → nazwa subdomeny →
**add domain** → w polu *current ip* adres IPv4 serwera → **update ip**. Sprawdzenie:

```bash
dig +short zai-iot26z.duckdns.org
```

## 2. Przygotowanie serwera (jednorazowo, jako root)

```bash
# swap 1 GB – zapas pamięci dla 1 GB RAM
fallocate -l 1G /swapfile && chmod 600 /swapfile && mkswap /swapfile && swapon /swapfile
echo '/swapfile none swap sw 0 0' >> /etc/fstab
sysctl vm.swappiness=10 && echo 'vm.swappiness=10' > /etc/sysctl.d/99-swappiness.conf

# zapora: tylko SSH, HTTP i HTTPS
ufw allow OpenSSH && ufw allow 80/tcp && ufw allow 443 && ufw --force enable

# automatyczne aktualizacje bezpieczeństwa
apt-get update && apt-get install -y unattended-upgrades git

# Docker
curl -fsSL https://get.docker.com | sh
docker run --rm hello-world
```

Jeśli skrypt Dockera nie obsługuje jeszcze danej wersji Ubuntu: `apt-get install -y docker.io docker-compose-v2`.

## 3. Pierwsze uruchomienie

```bash
git clone https://github.com/Pyruus/zai-iot-26z.git /opt/zai
cd /opt/zai/deploy
cp .env.example .env
sed -i "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$(openssl rand -hex 24)/" .env
nano .env                       # DOMAIN=<twoja subdomena>
docker compose pull
docker compose up -d
docker compose logs -f api      # Ctrl+C po komunikacie „Now listening on”
curl https://<domena>/api/health
```

Konto administratora (hasło podawane bez echa, nie trafia do historii powłoki):

```bash
read -rs ZAI_ADMIN_PASSWORD && export ZAI_ADMIN_PASSWORD
docker compose exec -e ZAI_ADMIN_PASSWORD api dotnet Zai.Api.dll create-admin --username admin
unset ZAI_ADMIN_PASSWORD
```

## 4. Dane przykładowe i testy (z komputera lokalnego)

```bash
ZAI_API_URL=https://<domena> ZAI_ADMIN_PASS='…' python3 generator/seed_demo.py
node tests/zai-tests.mjs --app https://<domena> --user admin --pass '…' --stage E2
```

## 5. Aktualizacja i zamrożenie wersji

Każdy push na `main` publikuje obrazy `latest` i `sha-<commit>`. Aktualizacja serwera:

```bash
cd /opt/zai && git pull
cd deploy && docker compose pull api && docker compose up -d api && docker image prune -f
```

Po zgłoszeniu etapu wersję produkcji należy przypiąć: `API_TAG=sha-<commit>` w `.env`, wtedy kolejne pushe
nie zmieniają działającej aplikacji.

## 6. Kopie zapasowe

```bash
chmod +x /opt/zai/deploy/backup.sh
( crontab -l 2>/dev/null; echo '30 3 * * * /opt/zai/deploy/backup.sh' ) | crontab -
```

Kopie (`pg_dump`, format custom) trafiają do `deploy/backups/` i są przechowywane 14 dni. Odtworzenie:
`docker compose exec -T db pg_restore -U zai -d zai --clean < backups/<plik>.dump`.
