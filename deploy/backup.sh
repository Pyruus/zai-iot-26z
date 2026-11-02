#!/usr/bin/env sh
set -eu
cd "$(dirname "$0")"
mkdir -p backups
docker compose exec -T db sh -c 'pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format=custom' \
  > "backups/zai-$(date +%Y%m%d-%H%M).dump"
find backups -name 'zai-*.dump' -mtime +14 -delete
