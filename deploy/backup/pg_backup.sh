#!/usr/bin/env bash
#
# Buildix PostgreSQL backup — kunlik dump, 7 kunlik rotatsiya.
#
# Ishlatish (serverda, compose bilan bir joyda):
#   chmod +x deploy/backup/pg_backup.sh
#   ./deploy/backup/pg_backup.sh
#
# Cron (har kuni 03:00 da) — `crontab -e`:
#   0 3 * * * cd /opt/buildix && ./deploy/backup/pg_backup.sh >> /var/log/buildix-backup.log 2>&1
#
set -euo pipefail

# --- Sozlamalar (ENV orqali override qilinadi) ---
COMPOSE_PROJECT="${COMPOSE_PROJECT:-buildix}"      # docker compose project nomi
DB_SERVICE="${DB_SERVICE:-db}"                      # compose'dagi db servis nomi
POSTGRES_USER="${POSTGRES_USER:-buildix}"          # .env dagi POSTGRES_USER bilan mos
POSTGRES_DB="${POSTGRES_DB:-buildix}"              # .env dagi POSTGRES_DB bilan mos
BACKUP_DIR="${BACKUP_DIR:-/var/backups/buildix}"   # dumplar shu yerga yoziladi
RETENTION_DAYS="${RETENTION_DAYS:-7}"              # nechа kun saqlash

# --- Bajarilishi ---
TS="$(date +%Y%m%d-%H%M%S)"
OUT="${BACKUP_DIR}/buildix-${TS}.sql.gz"

mkdir -p "${BACKUP_DIR}"

echo "[$(date -Is)] Backup boshlandi -> ${OUT}"

# docker exec ichida pg_dump; -Fc o'rniga plain+gzip (portativ va grep qilinadi).
docker exec -i "${COMPOSE_PROJECT}-${DB_SERVICE}-1" \
  pg_dump -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" \
  | gzip -9 > "${OUT}"

# Bo'sh/buzuq dump'ni ushlash
if [ ! -s "${OUT}" ]; then
  echo "[$(date -Is)] XATO: dump bo'sh!" >&2
  rm -f "${OUT}"
  exit 1
fi

echo "[$(date -Is)] Backup tayyor: $(du -h "${OUT}" | cut -f1)"

# Eski dumplarni tozalash
find "${BACKUP_DIR}" -name "buildix-*.sql.gz" -type f -mtime "+${RETENTION_DAYS}" -delete
echo "[$(date -Is)] ${RETENTION_DAYS} kundan eski dumplar o'chirildi."

# --- Off-site nusxa (ixtiyoriy) ---
# Server o'chsa ma'lumot yo'qolmasligi uchun dumpni boshqa joyga ko'chiring.
# Masalan S3 (awscli o'rnatilgan bo'lsa):
#   aws s3 cp "${OUT}" "s3://your-bucket/buildix/"
# yoki rsync bilan boshqa serverga:
#   rsync -a "${OUT}" backup@backup-host:/backups/buildix/

echo "[$(date -Is)] Yakunlandi."
