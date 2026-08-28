# Buildix — Deployment

Full stack in one `docker compose`: **PostgreSQL + .NET 9 API + nginx** (serves the
React SPA and reverse-proxies `/api` + `/hubs`). The API applies its EF migrations
on startup, so the schema is created/updated automatically on first boot.

```
Browser ──▶ nginx (web:80/443) ──┬── /api/*  ──▶ api:8080 (Kestrel)
                                 ├── /hubs/* ──▶ api:8080 (SignalR / WebSocket)
                                 └── /*      ──▶ SPA (static, try_files → index.html)
                                                    api ──▶ db:5432 (Postgres)
```

## 1. First run

```bash
cp .env.example .env    # edit: DB password, JWT_KEY (≥32), SUPERADMIN_* (see §3)
docker compose up -d --build
```

- SPA + API: **https://localhost/** — TLS is on by default. Without a real
  certificate the container generates a temporary self-signed one at start-up, so
  the browser warns until you complete §2. Plain `http://` redirects to `https://`.
- Only the **web** container publishes ports (80 + 443). `db` and `api` are
  reachable only on the internal compose network.
- Health: `docker compose ps` shows every service's state; the API's own check is
  `docker compose exec api curl -fsS http://localhost:8080/health` (it pings the
  DB). `web` waits for `api` to become *healthy* before it starts, so the first
  request after a deploy never lands on an upstream that is still migrating.
  `/api/health` is proxied but **restricted to private networks** — it is for
  monitoring, not for the public.

## 2. Production TLS (Let's Encrypt)

TLS is the default configuration (`NGINX_CONF=default.ssl.conf`); this section
only replaces the temporary self-signed certificate with a real one.

Point your domain's A record at the server **first** — the ACME check fetches a
file over plain HTTP from that domain.

```bash
# 1. Issue the certificate (nginx keeps serving; the ACME path is exempt from the
#    HTTPS redirect).
docker compose --profile tls run --rm certbot certonly --webroot \
  -w /var/www/certbot -d buildix.uz -d www.buildix.uz \
  --email you@example.com --agree-tos --no-eff-email

# 2. Put it where nginx reads certificates from, replacing the self-signed pair.
sudo cp deploy/nginx/letsencrypt/live/buildix.uz/fullchain.pem deploy/nginx/certs/
sudo cp deploy/nginx/letsencrypt/live/buildix.uz/privkey.pem   deploy/nginx/certs/

# 3. Reload.
docker compose restart web
```

**Renewal** (certificates last 90 days) — a daily cron/systemd timer:

```cron
0 3 * * * cd /srv/buildix && docker compose --profile tls run --rm certbot renew --quiet && cp deploy/nginx/letsencrypt/live/buildix.uz/*.pem deploy/nginx/certs/ && docker compose restart web
```

`.NET`'s `UseHttpsRedirection` stays **off by design** — nginx terminates TLS and
owns the HTTP→HTTPS redirect (a second redirect at Kestrel loops). The API trusts
`X-Forwarded-Proto`/`-For` from nginx (Program.cs ForwardedHeaders). HSTS
(`max-age=15552000`) is sent only on the HTTPS server block, so a certificate
problem never locks the site out of the browser permanently.

Falling back to plain HTTP for a local run: `NGINX_CONF=default.conf` in `.env`.

## 2b. Behind an existing reverse proxy (several projects on one server)

If the machine already runs other projects behind a shared nginx/Traefik/Caddy,
that proxy owns 80 and 443 — Buildix must not fight it for them. Buildix then
serves **plain HTTP on a loopback port**, and the existing proxy terminates TLS.

In `.env`:

```dotenv
NGINX_CONF=default.conf        # TLS is the outer proxy's job
WEB_HTTP_PORT=127.0.0.1:8090   # keep 127.0.0.1 — see below
WEB_HTTPS_PORT=127.0.0.1:8453  # unused with default.conf, parked out of the way
```

`127.0.0.1:` is not cosmetic. Without it Docker publishes the port on every
interface **and inserts its own iptables rule that bypasses a host firewall** —
the container becomes reachable at `http://<server-ip>:8090`, straight past the
proxy that enforces TLS, HSTS and any IP restrictions.

Then add one vhost to the existing proxy (nginx shown):

```nginx
server {
    listen 443 ssl;
    http2 on;
    server_name buildix.uz www.buildix.uz;

    ssl_certificate     /etc/letsencrypt/live/buildix.uz/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/buildix.uz/privkey.pem;

    # Product images are a few MB; the 1m default would 413 them.
    client_max_body_size 20m;

    location / {
        proxy_pass         http://127.0.0.1:8090;
        proxy_http_version 1.1;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Real-IP         $remote_addr;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;   # <- the outer proxy is the
                                                        #    one that knows it's TLS
        # SignalR lives under /hubs and needs the upgrade to pass through. Without
        # these two headers the hub silently falls back to long-polling, or fails.
        proxy_set_header   Upgrade    $http_upgrade;
        proxy_set_header   Connection $connection_upgrade;
        proxy_read_timeout 3600s;
    }
}

server {
    listen 80;
    server_name buildix.uz www.buildix.uz;
    location /.well-known/acme-challenge/ { root /var/www/certbot; }
    location / { return 301 https://$host$request_uri; }
}
```

`$connection_upgrade` comes from a `map` in the `http` block — most multi-project
proxies already have one; if not, add it once:

```nginx
map $http_upgrade $connection_upgrade { default upgrade; '' close; }
```

Certificates are issued by the **host** proxy's own certbot, not by the certbot
service in this compose file (§2 applies only when Buildix owns 80/443).

Two hops now sit in front of Kestrel (host proxy → Buildix nginx → api). That is
already accounted for: `ForwardLimit = 2` in `Program.cs`, and Buildix's
`default.conf` forwards the *outer* `X-Forwarded-Proto` instead of overwriting it
with its own (always-`http`) scheme.

**Check after deploying:**

```bash
curl -sI https://buildix.uz | head -3            # 200 from the SPA
curl -s  https://buildix.uz/api/Auth/Login -X GET -o /dev/null -w '%{http_code}\n'   # 405 = the API answered
ss -tlnp | grep 8090                             # must show 127.0.0.1:8090, never 0.0.0.0
```

## 3. Configuration & secrets

All secrets come from `.env` (gitignored) → compose env vars. Never bake them into
an image.

| Variable | Required | Notes |
|----------|----------|-------|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | yes | Postgres + API connection string |
| `JWT_KEY` | yes | ≥ 32 chars — API fails fast otherwise. `openssl rand -base64 48` |
| `SUPERADMIN_CONSOLE_SEGMENT` | yes | Secret URL segment of the platform console. `openssl rand -hex 16` |
| `SUPERADMIN_USERNAME` / `SUPERADMIN_PASSWORD` | yes | Seeded on first boot; change the password after signing in |
| `NGINX_CONF` | no | `default.ssl.conf` (default, TLS) or `default.conf` (plain HTTP, local only) |
| `LOG_RETENTION_DAYS` | no | Days of `app_logs` kept; purged daily. `0` disables. Default `30` |
| `TELEGRAM_BOT_TOKEN` | no | Empty disables Telegram notifications |
| `TELEGRAM_WEBHOOK_SECRET` | for webhook | Validated against `X-Telegram-Bot-Api-Secret-Token`; register with `setWebhook secret_token` |
| `TELEGRAM_DAILY_SUMMARY_HOUR` | no | Tashkent hour (0–23) the automatic day summary is sent at. Default `21` |

### SuperAdmin console

The platform console (stores, subscriptions, payments, platform settings) is not
a public route: it lives under `/_sa/<SUPERADMIN_CONSOLE_SEGMENT>/…`, and every
request whose segment does not match returns **404 before authentication** — a
scanner cannot tell the console exists. On top of that, each endpoint requires the
`SuperAdmin` role.

The superadmin never types that URL: signing in on the ordinary login page
(`https://<host>/login`) with `SUPERADMIN_USERNAME` / `SUPERADMIN_PASSWORD` hands
the segment to that account only (`GET /api/Auth/ConsoleSegment`) and redirects
into the console. If `SUPERADMIN_CONSOLE_SEGMENT` is unset, the login page says
the console is not configured instead of failing silently.

Rotating the segment: change the variable, `docker compose up -d api`. Old links
stop working immediately; open sessions keep their token but must sign in again to
learn the new URL.

### Telegram bot setup

1. Create the bot with [@BotFather](https://t.me/BotFather) → put the token in
   `TELEGRAM_BOT_TOKEN`.
2. Pick a random `TELEGRAM_WEBHOOK_SECRET` (e.g. `openssl rand -hex 32`) and
   register the webhook — without the secret the endpoint **fails closed** and
   ignores every update:

   ```bash
   curl -X POST "https://api.telegram.org/bot<TOKEN>/setWebhook" \
     -d "url=https://<your-host>/api/telegram/webhook" \
     -d "secret_token=<TELEGRAM_WEBHOOK_SECRET>"
   ```
3. **One bot serves every market.** The employee just writes to the bot — the
   Telegram id is read from the message itself, never typed. If that id isn't
   linked yet, the bot answers with it; the employee pastes it into
   **Аккаунт → Telegram ID** in the panel (`User.TelegramChatId`, unique
   platform-wide) and is recognised from then on. The id yields their market
   *and* their permissions. A chat with no match learns nothing about any shop.

#### Bot buttons

The bot answers with a button keyboard — no commands to remember. It shows only
what that user is allowed to run:

| Button | Returns | Permission |
|--------|---------|------------|
| 📊 Kunlik savdo | Today's sales — summary text + **Excel** | `sales.access` |
| 💰 Qarzdorlar | Debtors — **Excel** | `debts.access` |
| 📦 Kam qolgan | Low-stock products — **Excel** | `products.access` |
| 🧾 Faktura | Asks for a receipt number, then sends the **PDF** invoice | `sales.invoice` |

Sending a bare receipt number (e.g. `29`) also returns that invoice. The old
slash-commands (`/savdo`, `/qarz`, `/qoldiq`, `/faktura 29`) still work as
aliases.

Cost and profit columns follow `data.costPrice` / `data.profit`, so a cashier's
workbook never carries the shop's margins.

#### Automatic messages

- **Day summary** — once a day after `TELEGRAM_DAILY_SUMMARY_HOUR`, to the owner:
  summary text with the day's sales workbook attached. Gated by the market's
  «Сводка за день» setting.
- **Low stock** — **once per product**, to every user with `products.access` who
  linked an id and kept stock notifications on. The product is re-armed only when
  its quantity recovers above the minimum, so a shop hovering at the threshold is
  not spammed.

Production CORS origins live in `appsettings.Production.json`
(`Cors:AllowedOrigins` → `buildix.uz`); the SPA is same-origin behind nginx so it
needs no CORS. Add a separate-origin client with
`Cors__AllowedOrigins__0=https://…` as an api env var.

## 4. Data & operations

- **Volumes:** `pgdata` (Postgres data) and `uploads` (product images at
  `/app/wwwroot/uploads`) persist across deploys.
- **Migrations:** applied automatically on API startup. When scaling `api` to more
  than one replica, run migrations as a separate one-shot step instead (the
  in-process migrate races on concurrent boot).
- **Backup (do set this up):** schedule `pg_dump`, e.g.
  ```bash
  docker compose exec -T db pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" > backup-$(date +%F).sql
  ```
  Restore into a fresh db: `docker compose exec -T db psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" < backup.sql`.
- **Logs:** `docker compose logs -f api`. Warning-and-above also goes to the
  `app_logs` table; the API purges rows older than `LOG_RETENTION_DAYS` (default
  30) once a day, so the table cannot fill the disk.
- **Rebuild after code changes:** `docker compose up -d --build`

## 5. Desktop yangilanishlari

Do'kon ilovasi (`Buildix.Desktop`) yangi versiyani shu serverdan oladi.
Velopack'ga statik papkadan boshqa hech narsa kerak emas.

**Nega birinchi o'rnatish flesh-disk bilan.** To'plam 124 MB, delta esa
0,3 MB. Ya'ni faqat birinchi o'rnatish og'ir; undan keyingi har bir
yangilanish deyarli hech narsa turmaydi. Do'kon interneti sekin bo'lsa
birinchi o'rnatuvchini qo'lda olib borish soatlab kutishdan tez.

### Chiqarish

```powershell
# 1. Ish kompyuterida to'plamni yig'ish (Windows).
./deploy/build-desktop.ps1 -Version 0.2.0
#    → artifacts/Buildix-win-Setup.exe        ← flesh-diskka, yangi do'konlar uchun
#    → artifacts/Buildix-0.2.0-delta.nupkg    ← serverga, mavjud do'konlar uchun
#    → artifacts/releases.win.json
```

**Birinchi yig'ish uzoqroq davom etadi:** skript PostgreSQL ning rasmiy
binarlarini (~330 MB) yuklab oladi va `%LocalAppData%\Buildix-build-cache`
ga qo'yadi. Keyingi yig'ishlar shu keshdan foydalanadi. Uzilsa, keyingi
urinish o'sha yerdan davom etadi.

To'plam ichida PostgreSQL 17.6 keladi — do'konda hech narsa alohida
o'rnatilmaydi. Baza `%ProgramData%\Buildix\pgdata` da yashaydi va
yangilanish unga tegmaydi. Yonida Visual C++ ish vaqti kutubxonalari ham
boradi: ularsiz toza Windows'da baza umuman ishga tushmasdi.

```bash
# 2. Serverga ko'chirish. <MAXFIY> — taxmin qilib bo'lmaydigan papka nomi
#    (masalan `uuidgen` natijasi). Papka ochiq qoldirilsa mahsulotni istagan
#    odam yuklab oladi; autoindex o'chiq, ya'ni yo'lni bilmasdan topib
#    bo'lmaydi.
scp artifacts/*.nupkg artifacts/releases.win.json artifacts/RELEASES \
    artifacts/Buildix-win-Setup.exe \
    server:/srv/buildix/updates/<MAXFIY>/
```

O'rnatuvchi ham shu yerga qo'yiladi: do'kon egasi uni **panelning o'zidan**
yuklab oladi (Sozlamalar → «Do'kon dasturi»). Buning uchun `.env` da manzil
ko'rsatiladi:

```dotenv
DESKTOP_INSTALLER_URL=https://<domen>/updates/<MAXFIY>/Buildix-win-Setup.exe
DESKTOP_VERSION=1.0.0
```

```bash
docker compose up -d api        # manzil o'zgargach
```

Manzilni faqat kirgan EGA oladi (`GET /api/Markets/desktop-app`, Owner
huquqi), shuning uchun papka nomi sir bo'lib qolaveradi. Sozlanmagan bo'lsa
sahifada tugma o'rniga «hali tayyor emas» chiqadi — bu xato emas.

**O'rnatuvchi fayl nomi versiyasiz** (`Buildix-win-Setup.exe`) va u har
chiqarishda almashadi. Shuning uchun nginx uni keshlamaydi: aks holda ega
o'ttiz kun davomida eski o'rnatuvchini olardi va buni sezmasdi ham.
`.nupkg` fayllari esa nomida versiya bilan keladi va uzoq keshlanadi.


ESKI PAKETLARNI O'CHIRMANG. Delta faqat oldingi versiya paketiga nisbatan
qo'llanadi. 0.1.0 dagi do'kon bir necha oy yangilanmagan bo'lsa, unga
oraliq paketlar kerak bo'ladi.

### Har do'konda bir marta

Manzil SOZLASH OYNASIDAN qo'yiladi — faylni qo'lda tahrirlash shart emas:

```
Buildix.Desktop.exe --setup
```

Oynadagi «Yangilanish» maydoniga manzilni yozing:
`https://<domen>/updates/<MAXFIY>/`

Manzil sozlanmagan bo'lsa ilova sarlavhasida «yangilanish manzili
sozlanmagan» deb turadi — o'rnatishda uni unutib qoldirish jimgina o'tib
ketmasligi uchun.

Qo'lda tahrirlash ham mumkin (`%ProgramData%\Buildix\desktop.json`), lekin
fayl administrator huquqini talab qiladi — ichida baza paroli bor:

```json
{
  "Database:Password": "…o'zgartirmang…",
  "UpdateFeedUrl": "https://<domen>/updates/<MAXFIY>/"
}
```

Manzil bo'lmasa yangilanish umuman tekshirilmaydi va ilova to'liq
ishlayveradi. Bu ataylab shunday: sinov do'konini alohida papkaga
(`/updates/<BOSHQA-MAXFIY>/`) yo'naltirib, yangi versiyani avval o'sha
yerda tekshirish mumkin.

### Nima kutish kerak

Ilova ochilganda fonda tekshiradi va topsa jimgina yuklab oladi. Savdo
to'xtamaydi — sarlavhada shunchaki eslatma paydo bo'ladi. Yangi versiya
ilova KEYINGI safar ochilganda o'rnatiladi. Internet yo'q bo'lsa xato
chiqmaydi.

### Do'konda bir nechta kassa

Bir do'konda 1–3 kassa bo'lishi mumkin va ularning hammasi **bitta bazaga**
ishlaydi. Har kassada alohida baza bo'lsa, ikkisi bir vaqtda oxirgi qop
sementni sotib yuborardi va chek raqamlari to'qnashardi — qoldiq qulfi ham,
raqam qulfi ham faqat bitta baza ichida ishlaydi.

```
Buildix.Desktop.exe --setup
```

**Server kassada** (bittasi): «Bu kompyuter — SERVER» + «Boshqa kassalar shu
kompyuterga ulanadi» belgisi + «Tarmoq ruxsatini ochish» (UAC so'raydi,
brandmauerda 5088-portni faqat xususiy tarmoq uchun ochadi). Oynada shu
kompyuterning manzili ko'rsatiladi — boshqa kassalarda aynan shu yoziladi.

**Qolgan kassalarda**: «Server kassaga ULANADI» + manzil + «Tekshirish».
Tekshiruv serverga haqiqiy so'rov yuboradi, ya'ni xato manzil do'kondan
chiqib ketishdan oldin bilinadi.

Ulanuvchi kassada na baza, na API ko'tariladi — u faqat oyna. Shu sababli
tezroq ochiladi. Aloqa uzilsa ekranda tushunarli xabar chiqadi va aloqa
tiklanishi bilan ish o'zi davom etadi; kassirdan hech narsa talab
qilinmaydi.

Sozlama `%ProgramData%\Buildix\desktop.json` da saqlanadi — nashr fayllarida
emas, ya'ni yangilanish unga tegmaydi.

### Yorliq printeri

Tovar yorliqlari 58×40, 40×30 yoki 30×20 mm rulonga bosiladi. Qog'ozga
XATO o'lcham urilishining sababi deyarli har doim bitta: brauzerning chop
etish oynasi sukut bo'yicha «sahifaga moslash» qiladi va Windows'dagi
printer qog'ozi A4 bo'lsa, kichkina yorliq A4 ga cho'ziladi. Yorliq PDF ining
o'zi har doim to'g'ri o'lchamda chiqadi — buni sinov millimetrigacha
tekshiradi.

Endi ikki himoya bor:

1. **Har joyda.** Yorliq aniq `@page { size: 58mm 40mm; margin: 0 }` yozilgan
   sahifaga qo'yiladi — brauzer o'lchamni drayverga o'zi aytadi va masshtab
   qo'llamaydi.

2. **Do'kon dasturida.** Sozlamada yorliq printerini bir marta tanlang:

   ```
   Buildix.Desktop.exe --setup   →   «Yorliq printeri»
   ```

   Shundan keyin yorliq chop etish oynasisiz, to'g'ridan-to'g'ri o'sha
   printerga, aniq o'lchamda chiqadi. Do'konda odatda ikkita printer bo'ladi
   (chek va yorliq) va oynada har safar to'g'risini tanlash kerak edi.

Tanlanmagan bo'lsa avvalgidek oyna ochiladi — ish to'xtamaydi. Oynada
**Masshtab: 100%** (yoki «Haqiqiy o'lcham») ekaniga ishonch hosil qiling va
Windows'dagi printer sozlamasida qog'oz o'lchami rulonga mos bo'lsin: bu ikki
qiymat drayver darajasida bo'lib, ularni ilova o'zgartira olmaydi.

### Kunlik zaxira nusxa

Ilova ochilganda oxirgi nusxa 20 soatdan eski bo'lsa, fonda yangisini oladi:
`%ProgramData%\Buildix\backups\buildix-YYYY-MM-DD-HHmm.dump`. Oxirgi 14 tasi
saqlanadi, eskilari o'chiriladi.

Jadval bo'yicha emas, **ochilishda** — do'kon kompyuteri kechasi o'chiriladi
va «har kuni soat 02:00 da» degan jadval hech qachon ishlamasdi.

**Nimadan himoya qiladi:** xato bilan o'chirilgan ma'lumot, buzilgan baza.
**Nimadan himoya qilmaydi:** disk ishdan chiqishi, kompyuter o'g'irlanishi —
nusxa o'sha diskda yotadi. Shuning uchun bu bulutga sinxronizatsiyaning
o'rnini bosmaydi.

Tiklash (ilova YOPIQ bo'lishi shart emas, lekin savdo to'xtatilsin):

```powershell
$bin = "$env:LOCALAPPDATA\Buildix\current\pg\bin"
$env:PGPASSWORD = (Get-Content "$env:ProgramData\Buildix\desktop.json" -Raw | ConvertFrom-Json).'Database:Password'

# Yangi bazaga tiklash - mavjudini buzmasdan tekshirish uchun
& "$bin\createdb.exe" -h 127.0.0.1 -p 5433 -U buildix buildix_tiklangan
& "$bin\pg_restore.exe" -h 127.0.0.1 -p 5433 -U buildix -d buildix_tiklangan `
    "$env:ProgramData\Buildix\backups\buildix-2026-08-25-1200.dump"
```

Tekshirgach, ishlaydigan bazani almashtirish uchun ilovani yoping, eski
`buildix` bazasini boshqa nomga o'tkazing va tiklanganini `buildix` deb
nomlang. **Har chorakda bir marta shu mashqni o'tkazing** — sinalmagan
zaxira zaxira emas.

### Hozircha imzo yo'q

O'rnatuvchi kod imzosi bilan imzolanmagan, shuning uchun Windows
SmartScreen «Windows protected your PC» ogohlantirishini ko'rsatadi
(«Batafsil» → «Baribir ishga tushirish»). Buni o'rnatuvchi usta bosib
o'tadi, mijoz emas. Sertifikat masalasi keyinga qoldirilgan.

## Files

| Path | Purpose |
|------|---------|
| `docker-compose.yml` | db + api + web services, volumes |
| `.env.example` | secret/config template → copy to `.env` |
| `Buildix.API/Dockerfile` | multi-stage .NET build (context = repo root) |
| `Buildix.Web/Dockerfile` | SPA build → nginx static |
| `deploy/nginx/default.ssl.conf` | TLS edge config — **the default** |
| `deploy/nginx/default.conf` | plain-HTTP edge config (local runs only) |
| `deploy/nginx/ensure-cert.sh` | start-up step: temporary self-signed cert if none is mounted |
| `deploy/nginx/certs/` | certificates nginx reads (`fullchain.pem` + `privkey.pem`), gitignored |
| `deploy/build-desktop.ps1` | do'kon uchun o'rnatuvchi va yangilanish paketlarini yig'adi (Windows) |
| `deploy/updates/` | yangilanish paketlari turadigan papka (`UPDATES_DIR` bilan almashtiriladi), gitignored |
