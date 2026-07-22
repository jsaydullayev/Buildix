# Buildix.Web

Buildix ERP tizimining web frontend qismi — **React 18 + TypeScript + Vite + Tailwind CSS**.
`Buildix.API` (.NET 9) backendiga ulanadigan SPA.

> To'liq reja va arxitektura: [PLAN.md](./PLAN.md)

## Talablar

- Node.js ≥ 20 (ishlab chiqilgan: v24)
- Ishlab turgan `Buildix.API` (default `http://localhost:8080`)

## Boshlash

```bash
cd Buildix.Web
cp .env.example .env      # kerak bo'lsa tahrirlang
npm install
npm run dev               # http://localhost:5173
```

Dev-serverda `/api` va `/hubs` avtomatik `localhost:8080` ga proxy qilinadi
(`vite.config.ts`). API boshqa portda bo'lsa: `VITE_API_PROXY_TARGET` ni o'rnating.

## Skriptlar

| Skript | Vazifa |
|---|---|
| `npm run dev` | Dev-server (HMR) |
| `npm run build` | Prod build (`dist/`) |
| `npm run preview` | Prod buildni lokal ko'rish |
| `npm run typecheck` | TypeScript tekshiruvi |
| `npm run lint` | ESLint |
| `npm run format` | Prettier |
| `npm run test` | Vitest |

## Struktura

```
src/
  app/       — router, providers, layouts
  shared/    — api, auth, ui, lib, i18n, config, realtime
  features/  — landing, auth, dashboard, sales, warehouse, debts,
               purchases, shifts, reports, employees, settings, account, superadmin
```

## Design system

Dizayn token'lari (`docs/WebDesign` maketlaridan) `tailwind.config.ts` da:
primary `#2563eb`, sidebar navy `#0f2557`, fon `#f6f8fb`, karta radius `13px`;
fontlar `Golos Text` (body) + `Unbounded` (brand). Komponentlar faqat token nomlariga
murojaat qiladi (`bg-surface`, `text-muted`, `rounded-card`), xom hex ishlatilmaydi.
