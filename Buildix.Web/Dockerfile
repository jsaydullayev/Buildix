# syntax=docker/dockerfile:1
# Buildix SPA (React + Vite + TS) → static bundle served by nginx.
# The same nginx also reverse-proxies /api and /hubs to the API container, so the
# browser only ever talks to one origin (matches the dev vite proxy layout).
# Build context: ./Buildix.Web (docker-compose sets it).

# ── build ────────────────────────────────────────────────────────────────
FROM node:22-alpine AS build
WORKDIR /app
# Cached install: only re-runs when the lockfile changes.
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
# `build` = tsc -b && vite build. The app reads /api and /hubs as same-origin
# (src/shared/config/env.ts defaults), so no build-time API URL is required.
RUN npm run build

# ── runtime ──────────────────────────────────────────────────────────────
FROM nginx:1.27-alpine AS runtime
# Static bundle. The server config (SPA fallback + /api + /hubs proxy) is
# bind-mounted by compose to /etc/nginx/conf.d/default.conf so it can be swapped
# (HTTP ↔ TLS) without rebuilding the image.
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80 443
