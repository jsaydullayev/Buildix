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

## 1. First run (HTTP, local / staging)

```bash
cp .env.example .env          # then edit: strong DB password + JWT_KEY (≥32 chars)
docker compose up -d --build
```

- SPA + API: http://localhost/
- Only the **web** container publishes ports (80). `db` and `api` are reachable
  only on the internal compose network.
- Health: `docker compose exec api curl -fsS http://localhost:8080/health`

## 2. Production TLS (nginx + certificates)

The default nginx config is HTTP-only so the stack runs without certs. For
production, switch to the TLS config:

1. Obtain certs (e.g. certbot / Let's Encrypt) as `fullchain.pem` + `privkey.pem`
   and place them in `deploy/nginx/certs/`.
2. In `docker-compose.yml` (web service): publish `443:443` and mount the certs
   (`./deploy/nginx/certs:/etc/nginx/certs:ro`) — both lines are present, commented.
3. Point the config bind-mount at the TLS variant, e.g. copy it into place:
   ```bash
   cp deploy/nginx/default.ssl.conf.example deploy/nginx/default.conf
   ```
   (or change the bind-mount source). It matches `server_name buildix.uz
   www.buildix.uz` and the `docs/TZ-sub-path-login-va-obuna.md §9` layout.
4. `docker compose up -d web`

`.NET`'s `UseHttpsRedirection` stays **off by design** — nginx terminates TLS and
owns the HTTP→HTTPS redirect (a second redirect at Kestrel loops). The API trusts
`X-Forwarded-Proto`/`-For` from nginx (Program.cs ForwardedHeaders).

## 3. Configuration & secrets

All secrets come from `.env` (gitignored) → compose env vars. Never bake them into
an image.

| Variable | Required | Notes |
|----------|----------|-------|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | yes | Postgres + API connection string |
| `JWT_KEY` | yes | ≥ 32 chars — API fails fast otherwise. `openssl rand -base64 48` |
| `TELEGRAM_BOT_TOKEN` | no | Empty disables Telegram notifications |
| `TELEGRAM_WEBHOOK_SECRET` | for webhook | Validated against `X-Telegram-Bot-Api-Secret-Token`; register with `setWebhook secret_token` |

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
- **Logs:** `docker compose logs -f api`
- **Rebuild after code changes:** `docker compose up -d --build`

## Files

| Path | Purpose |
|------|---------|
| `docker-compose.yml` | db + api + web services, volumes |
| `.env.example` | secret/config template → copy to `.env` |
| `Buildix.API/Dockerfile` | multi-stage .NET build (context = repo root) |
| `Buildix.Web/Dockerfile` | SPA build → nginx static |
| `deploy/nginx/default.conf` | HTTP edge config (default) |
| `deploy/nginx/default.ssl.conf.example` | TLS edge config (production) |
