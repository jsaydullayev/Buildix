#!/bin/sh
# Buildix — TLS'ni HAR DOIM ishga tushiradigan qadam.
#
# nginx TLS konfiguratsiyasi bilan sertifikatsiz ishga tushsa, konteyner
# butunlay yiqiladi va sayt umuman ochilmaydi. Shuning uchun sertifikat
# yo'q bo'lsa, bu skript vaqtinchalik O'ZI IMZOLAGAN sertifikat yaratadi:
# stack birinchi bosqichdanoq HTTPS'da ko'tariladi, brauzer esa ogohlantirish
# ko'rsatadi — ya'ni «hali haqiqiy sertifikat qo'yilmagan» degani ko'rinib
# turadi (ochiq HTTP'da bunday signal umuman bo'lmaydi).
#
# Haqiqiy sertifikat: `docker compose --profile tls run --rm certbot ...`
# (deploy/README.md). Certbot uni shu papkaga yozadi va keyingi qayta
# ishga tushishda mana shu skript uni tegmasdan qoldiradi.
set -eu

CERT_DIR=/etc/nginx/certs
CRT="$CERT_DIR/fullchain.pem"
KEY="$CERT_DIR/privkey.pem"

if [ -s "$CRT" ] && [ -s "$KEY" ]; then
    echo "[buildix] TLS sertifikati joyida — o'zgartirilmaydi."
    exit 0
fi

echo "[buildix] TLS sertifikati topilmadi — vaqtinchalik o'zi imzolagan sertifikat yaratilmoqda."
mkdir -p "$CERT_DIR"
openssl req -x509 -nodes -newkey rsa:2048 -days 365 \
    -keyout "$KEY" -out "$CRT" \
    -subj "/CN=buildix.local" \
    -addext "subjectAltName=DNS:buildix.local,DNS:localhost" 2>/dev/null
echo "[buildix] Vaqtinchalik sertifikat yaratildi. Haqiqiysini olish: deploy/README.md → TLS."
