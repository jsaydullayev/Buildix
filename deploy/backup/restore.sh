#!/usr/bin/env bash
#
# Buildix PostgreSQL restore — gzip dump'ni tiklaydi.
#
# Ishlatish:
#   ./deploy/backup/restore.sh /var/backups/buildix/buildix-20260724-030000.sql.gz
#
# DIQQAT: bu mavjud ma'lumot ustiga yozadi. Avval joriy holatdan backup oling.
#
set -euo pipefail

COMPOSE_PROJECT="${COMPOSE_PROJECT:-buildix}"
DB_SERVICE="${DB_SERVICE:-db}"
POSTGRES_USER="${POSTGRES_USER:-buildix}"
POSTGRES_DB="${POSTGRES_DB:-buildix}"

DUMP="${1:-}"
if [ -z "${DUMP}" ] || [ ! -f "${DUMP}" ]; then
  echo "Foydalanish: $0 <dump.sql.gz>" >&2
  exit 1
fi

echo "[$(date -Is)] '${DUMP}' -> ${POSTGRES_DB} tiklanmoqda..."
gunzip -c "${DUMP}" \
  | docker exec -i "${COMPOSE_PROJECT}-${DB_SERVICE}-1" \
      psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}"

echo "[$(date -Is)] Restore yakunlandi."
