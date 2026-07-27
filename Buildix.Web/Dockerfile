# syntax=docker/dockerfile:1
# Buildix SPA (React + Vite + TS) → static bundle served by nginx.
# The same nginx also reverse-proxies /api and /hubs to the API container, so the
# browser only ever talks to one origin (matches the dev vite proxy layout).
# Build context: the REPO ROOT (docker-compose sets `context: .`) — the runtime
# stage needs deploy/nginx/ensure-cert.sh, which is shared with the compose
# bind-mounts and must not be duplicated inside Buildix.Web/.

# ── build ────────────────────────────────────────────────────────────────
FROM node:22-alpine AS build
WORKDIR /app
# Cached install: only re-runs when the lockfile changes.
COPY Buildix.Web/package.json Buildix.Web/package-lock.json ./
RUN npm ci
COPY Buildix.Web/ .
# `build` = tsc -b && vite build. The app reads /api and /hubs as same-origin
# (src/shared/config/env.ts defaults), so no build-time API URL is required.
RUN npm run build

# ── runtime ──────────────────────────────────────────────────────────────
FROM nginx:1.27-alpine AS runtime
# openssl — sertifikat bo'lmaganda vaqtinchalik o'zi imzolaganini yaratish uchun
# (deploy/nginx/ensure-cert.sh). Ansiz TLS konfiguratsiyasi bilan konteyner
# birinchi ishga tushishda yiqilib qolardi.
# wget — konteyner healthcheck'i uchun (busybox tarkibida keladi).
RUN apk add --no-cache openssl
# Static bundle. The server config (SPA fallback + /api + /hubs proxy) is
# bind-mounted by compose to /etc/nginx/conf.d/default.conf so it can be swapped
# (HTTP ↔ TLS) without rebuilding the image.
COPY --from=build /app/dist /usr/share/nginx/html

# nginx rasmi ishga tushishdan OLDIN /docker-entrypoint.d/*.sh ni bajaradi.
COPY --chmod=755 deploy/nginx/ensure-cert.sh /docker-entrypoint.d/10-buildix-ensure-cert.sh

EXPOSE 80 443
