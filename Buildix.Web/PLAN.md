# Buildix.Web — Frontend Reja (PLAN)

> Buildix ERP tizimining **web frontend** qismi. Backend (`Buildix.API`, .NET 9) tayyor —
> bu loyiha unga ulanadigan alohida SPA (Single Page Application).
> Til: interfeys **RU** (default), lekin **UZ / RU / EN** i18n bilan quriladi.

---

## 1. Umumiy ma'lumot

| | |
|---|---|
| **Loyiha turi** | Ichki ERP admin-panel + ommaviy landing/login (SPA) |
| **Backend** | `Buildix.API` — REST + SignalR, JWT auth, camelCase JSON, Tashkent (GMT+5) vaqt |
| **Multi-tenant** | Sub-path model: `buildix.uz/{subdomain}/...` (DNS subdomain EMAS, path segment) |
| **Rollar** | `SuperAdmin(0)` · `Owner(1)` · `Admin(2)` · `Seller(3)` + fine-grained RBAC permissions |
| **Real-time** | SignalR hub `/hubs/sales` (draft-sotuvlar sinxroni) |
| **Dizayn manbasi** | `docs/WebDesign/*.dc.html` (HTML maketlar) + `docs/WebDesignPNG/*.png` (skrinshotlar) |

**Ekranlar (dizayndan):** Welcome (landing), Login, Панель (Dashboard), Продажи (POS/Sales),
Склад (Warehouse), Долги (Debts), Закуп (Purchases/Zakup), Смены (Shifts), Отчёты (Reports),
Сотрудники (Employees), Настройки (Settings), Мой аккаунт (Account) + SuperAdmin konsoli.

---

## 2. Texnologiyalar (Tech Stack)

| Qatlam | Tanlov | Sabab |
|---|---|---|
| **Framework** | React 18 + **TypeScript** | Keng ekosistema, murakkab jadval/dashboard, real-time |
| **Build** | **Vite** | Tez dev-server, HMR, yengil prod build |
| **Styling** | **Tailwind CSS** | Dizayndagi aniq token'lar (rang, radius, spacing) `theme` ga tushadi |
| **Routing** | **React Router v6** | Sub-path (`/:subdomain/...`), nested layout, route guards |
| **Server state** | **TanStack Query (React Query)** | Cache, retry, invalidation — REST bilan ideal |
| **Client state** | **Zustand** | Yengil — auth/session, UI (sidebar, modal) uchun |
| **Forms** | **React Hook Form + Zod** | Validatsiya, tip-xavfsiz sxema |
| **HTTP** | **Axios** (interceptor: auth + refresh + error-map) | Token refresh, 401/402/423 markazlashgan ishlov |
| **Real-time** | **@microsoft/signalr** | `/hubs/sales`, `?token=` bilan ulanish |
| **i18n** | **i18next + react-i18next** | UZ/RU/EN, `ru` default |
| **Charts** | **Recharts** (yoki yengil custom SVG) | Dashboard/Reports grafiklari |
| **Icons** | **lucide-react** | Dizayndagi stroke-ikonkalarga mos (Feather uslubi) |
| **Date** | **date-fns** + Tashkent util | Sana formatlash (server allaqachon GMT+5 qaytaradi) |
| **Table** | **TanStack Table** (kerak bo'lsa) | Склад/Продажи katta ro'yxatlar, pagination |
| **Lint/Format** | ESLint + Prettier + TypeScript strict | Kod sifati |
| **Test** | Vitest + React Testing Library (asosiy oqimlar) | Auth, POS, guards |

**Fontlar (Google Fonts):** `Golos Text` (body 400–700), `Unbounded` (brand/heading 500–700).

---

## 3. Loyiha strukturasi (Feature-based)

```
Buildix.Web/
├─ PLAN.md                     ← shu fayl
├─ index.html                  ← Google Fonts preconnect
├─ vite.config.ts              ← proxy /api → localhost:8080, alias @/
├─ tailwind.config.ts          ← design token'lar (§4)
├─ tsconfig.json               ← strict, path alias
├─ .env / .env.example         ← VITE_API_BASE_URL
├─ package.json
└─ src/
   ├─ main.tsx                 ← providers (QueryClient, Router, i18n)
   ├─ app/
   │  ├─ router.tsx            ← route tree + guards
   │  ├─ providers.tsx
   │  └─ layouts/              ← AppLayout (sidebar+topbar), AuthLayout, PublicLayout
   ├─ shared/
   │  ├─ api/                  ← axios client, interceptors, endpoints, types
   │  ├─ auth/                 ← useAuth, session store, RBAC guard, silent-login
   │  ├─ realtime/             ← SignalR connection hook
   │  ├─ ui/                   ← Button, Card, Input, Badge, Modal, Table, StatCard, Toggle…
   │  ├─ hooks/  utils/  i18n/ ← formatSum, formatDate, permissions
   │  └─ config/               ← constants, permission keys, nav items
   └─ features/
      ├─ landing/              ← Welcome (ommaviy)
      ├─ auth/                 ← Login, silent auto-login, subscription/blocked ekranlar
      ├─ dashboard/            ← Панель
      ├─ sales/                ← Продажи (POS + ro'yxat + draft)
      ├─ warehouse/            ← Склад (mahsulotlar, kategoriya, import)
      ├─ debts/                ← Долги
      ├─ purchases/            ← Закуп (zakup + suppliers)
      ├─ shifts/               ← Смены + kassa
      ├─ reports/              ← Отчёты
      ├─ employees/            ← Сотрудники (users)
      ├─ settings/             ← Настройки (market)
      ├─ account/              ← Мой аккаунт (profil + sessiyalar)
      └─ superadmin/           ← /_sa konsoli (requests, owners, markets)
```

---

## 4. Design System → Tailwind token'lar

Maketlardan (`Owner Dashboard.dc.html`, `Login.dc.html`, PNG'lar) chiqarilgan aniq qiymatlar.

**Ranglar**
| Token | Hex | Ishlatilishi |
|---|---|---|
| `primary` / `primary-hover` | `#2563eb` / `#1e40af` | tugma, aktiv holat, urg'u |
| `sidebar` (navy) | `#0f2557` | chap panel foni |
| `bg` | `#f6f8fb` | sahifa foni |
| `surface` | `#ffffff` | kartochka |
| `border` | `#e8edf4` | kartochka chegarasi |
| `hairline` | `#f1f5f9` | jadval ajratuvchi |
| `text` | `#0f172a` | asosiy matn |
| `muted` / `muted-2` | `#64748b` / `#94a3b8` | ikkilamchi matn |
| `label` | `#334155` | forma label |
| `input-border` | `#cbd5e1` | input chegarasi |
| `success` (+bg/border) | `#16a34a` (`#f0fdf4`/`#bbf7d0`) | naqd, "успешно", ▲ |
| `danger` | `#dc2626` | qarz/xato/просрочен |
| `warn` / `amber` | `#ea580c`·`#b45309` / `#f59e0b` | kam qoldiq |
| `info-bg` | `#eff6ff` | ikonka foni, "карта" |

**Tipografiya:** `font-body: 'Golos Text'`, `font-brand: 'Unbounded'`.
O'lchamlar: sahifa sarlavha 20px/600, stat raqam 23px/700, tan 13–15px, label 12–14.5px, micro 11–12px.

**Radius:** `card: 13px`, `input: 11px`, `btn: 9px`, `pill: 999px`.
**Spacing:** karta padding 20–24px, grid gap 16–18px, sidebar eni **232px**.
**Soya:** primary tugma `0 8px 20px rgba(37,99,235,.25)`; input focus ring `0 0 0 4px rgba(37,99,235,.14)`.

**Amaliy qadam:** yuqoridagilarni `tailwind.config.ts → theme.extend` (colors, borderRadius, fontFamily,
boxShadow) ga kiritamiz; `src/index.css` da CSS-variable sifatida ham e'lon qilamiz (theming zaxira).

---

## 5. API integratsiya

**Base:** `VITE_API_BASE_URL` (dev: Vite proxy `/api` → `http://localhost:8080`).
JSON **camelCase**; sanalar server tomonidan Tashkent (GMT+5) da beriladi.

**Axios interceptor mantiqi:**
1. **Request:** `Authorization: Bearer <accessToken>` (session store'dan).
2. **Response 401:** access token muddati o'tgan → `POST /api/Auth/RefreshToken { accessToken, refreshToken }`
   bilan yangilaymiz (bitta parallel refresh, navbat bilan retry). Muvaffaqiyatsiz → logout + login sahifasi.
3. **Response 402 `SUBSCRIPTION_EXPIRED`** → "Obuna tugagan" ekrani (owner uchun to'lov/aloqa).
4. **Response 423 `MARKET_BLOCKED`** → "Market bloklangan" ekrani.
5. **429** → "juda ko'p urinish" toast (`retryAfterSeconds`).
6. Global error-mapper: backend `{ statusCode, message }` → user-facing toast/inline.

**Auth oqimi (sub-path + silent login):**
- URL: `buildix.uz/{subdomain}/login`. Login `POST /api/Auth/Login { username, password, subdomain }`.
- `AuthResponse`: `userId, username, fullName, role, language, accessToken, refreshToken, expiresAt, permissions[], marketId, subdomain`.
- **Silent auto-login:** saqlangan sessiya `subdomain` bilan mos kelsa — login formani ko'rsatmasdan kiritamiz.
- Login sahifasi ochilishida `GET /api/public/market/{subdomain}` — market holati/nomi (obuna tugagan bo'lsa ogohlantirish).
- Sessiya saqlash: `accessToken` (memory), `refreshToken` + meta (localStorage, subdomain bo'yicha kalitlangan).

**RBAC (permission guard):** `AuthResponse.permissions[]` (masalan `sales.create`, `data.profit`,
`reports.access`). UI elementlari va route'lar shu ro'yxat bo'yicha yashiriladi/bloklanadi.
Owner/SuperAdmin — to'liq katalog.

**SignalR:** `/hubs/sales?token=<accessToken>` → `JoinBranchGroup(branchId)`,
`DraftSaleUpdated(sellerId, saleId)` eventi (POS'da draft sinxroni). Token refresh bo'lganda qayta ulanish.

### Endpoint xaritasi (feature → API)
| Feature | Asosiy endpointlar |
|---|---|
| Auth | `POST /api/Auth/Login·RefreshToken·Logout` |
| Public/Onboarding | `GET /api/public/market/{subdomain}` · `POST /api/RegistrationRequests` |
| Dashboard | `GET /api/Reports/dashboard-summary·weekly-series·top-products·cash-balance` |
| Sales (POS) | `GET /api/Sales · /{id} · by-date · my-drafts · my-unfinished · debtors`; `POST /api/Sales`, `.../items`, `.../items/remove`, `.../payments`, `.../cancel`, `.../mark-debt`, `.../return-item`, `.../apply-credit`; `PATCH .../customer · discount · items/price`; `GET /{id}/invoice` |
| Warehouse | `GET /api/Products · low-stock · units · export`; `POST/PUT/DELETE`; `POST /{id}/image`; `import/preview·confirm`; `ProductCategories` CRUD |
| Customers | `GET/POST/PUT/DELETE /api/Customers · phone/{phone} · export · {id}/soft-delete` |
| Debts | `GET /api/Debts/{customerId} · customer/{id}/total`; `POST /{debtId}/pay`; `PUT /{debtId}/due-date` |
| Purchases | `Zakups` CRUD · `Suppliers` CRUD/export |
| Shifts / Kassa | `GET /api/Shifts/current`, `POST open·close`; `GET /api/CashRegister · today-sales`, `POST withdraw·add` |
| Reports | `GET /api/Reports/daily·period·comprehensive·profit-summary·staff-performance·my-performance·monthly-category-sales` + PDF/Excel export |
| Employees | `Users` CRUD · `users.shift` |
| Settings | `Markets` (get/update) |
| Account | Users profil + `AuditLogs` (kirish tarixi/sessiyalar) |
| SuperAdmin | `/api/_sa/{consoleSegment}/requests·owners·markets/{id}/block·unblock` |

---

## 6. Routing & Guard modeli

```
/                              → Landing (Welcome) [public]
/:subdomain/login              → Login (+ market holati, silent auto-login)
/:subdomain/*                  → AppLayout [auth guard + subscription guard]
   ├─ dashboard                → Панель            [dashboard.access]
   ├─ sales                    → Продажи / POS     [sales.access]
   ├─ warehouse                → Склад             [products.access]
   ├─ debts                    → Долги             [debts.access]
   ├─ purchases                → Закуп             [zakup.access]
   ├─ shifts                   → Смены             [cashregister.access / users.shift]
   ├─ reports                  → Отчёты            [reports.access]
   ├─ employees                → Сотрудники        [users.access]
   ├─ settings                 → Настройки         [Owner]
   └─ account                  → Мой аккаунт       [all]
/_sa/{segment}/*               → SuperAdmin konsoli [SuperAdmin]
```
Guard'lar: **AuthGuard** (token yo'q → login), **SubscriptionGuard** (402/423 → tegishli ekran),
**RoleGuard / PermissionGuard** (ruxsat yo'q → 403 yoki nav'dan yashirish).

---

## 7. Bosqichlar (Milestones)

### Bosqich 0 — Poydevor (Setup)
- [ ] Vite + React + TS scaffold (`Buildix.Web/`), ESLint/Prettier/strict TS
- [ ] Tailwind o'rnatish + `tailwind.config.ts` ga §4 token'lar
- [ ] Google Fonts (Golos Text, Unbounded) `index.html` ga
- [ ] Folder struktura (§3), path alias `@/`, Vite proxy `/api`
- [ ] `.env.example`, providers (QueryClient, Router, i18n), asosiy `ui/` primitivlar

### Bosqich 1 — Auth & Shell (skelet)
- [ ] Axios client + auth/refresh/error interceptorlar (401/402/423/429)
- [ ] Session store (Zustand) + silent auto-login
- [ ] Login sahifasi (dizayn bo'yicha) + `public/market/{subdomain}`
- [ ] AppLayout: navy sidebar (232px) + topbar + market-switcher
- [ ] AuthGuard / SubscriptionGuard / PermissionGuard
- [ ] i18n karkas (ru/uz/en), `formatSum`, `formatDate`

### Bosqich 2 — Asosiy modullar (core)
- [ ] **Панель** (Dashboard): 4 stat-karta, haftalik grafik, oxirgi sotuvlar, kam qoldiq, to'lovlar
- [ ] **Склад** (Warehouse): jadval, qidiruv/filtr, kategoriya, low-stock, mahsulot CRUD + rasm
- [ ] **Продажи** (POS + ro'yxat): draft yaratish, item qo'shish/o'chirish, to'lov, chegirma, qarz, chek/invoice
- [ ] SignalR ulanish (DraftSaleUpdated)

### Bosqich 3 — Moliyaviy modullar
- [ ] **Долги** (Debts): mijoz qarzlari, to'lash, muddat
- [ ] **Закуп** (Purchases): zakup ro'yxati, "rekomenduem zakazat", suppliers
- [ ] **Смены** (Shifts): joriy smena, ochish/yopish, kassa (withdraw/add), tarix
- [ ] **Отчёты** (Reports): davr filtri, grafiklar, top-mahsulot, sotuvchilar, PDF/Excel eksport

### Bosqich 4 — Boshqaruv & Landing
- [ ] **Сотрудники** (Employees): users CRUD, ruxsatlar
- [ ] **Настройки** (Settings): market, kassa, склад, bildirishnomalar (toggle'lar)
- [ ] **Мой аккаунт** (Account): profil, parol, qurilmalar/sessiyalar, kirish tarixi
- [ ] **Welcome** (Landing): marketing sahifa + til almashtirgich
- [ ] **SuperAdmin konsoli**: requests approve/reject, owners, market block/unblock

### Bosqich 5 — Sayqal (Polish)
- [ ] i18n to'liq (uz/en tarjimalar), responsive (min 1280px, adaptiv)
- [ ] Loading/skeleton, empty/error holatlar, toast tizimi
- [ ] A11y (focus ring, klaviatura, ARIA), tugma/soya micro-detallar
- [ ] Asosiy oqim testlari (auth, POS, guard), prod build + deploy hujjati (nginx `/`)

---

## 8. Non-functional talablar
- **Til:** default `ru`; barcha matn i18n kalit orqali (hardcode yo'q).
- **Format:** sum `318 400 000` (bo'sh joy ajratuvchi); sana RU ("Суббота, 19 июля 2026").
- **Vaqt:** server GMT+5 qaytaradi — front qayta konvert qilmaydi.
- **Xavfsizlik:** access token faqat memory'da; refresh localStorage'da; logout'da token revoke (`POST /Auth/Logout`).
- **Responsive:** dizayn `min-width:1280px` (desktop-first ERP); keyin planshet moslashuvi.
- **Deploy:** statik build → nginx `/` (API allaqachon `/api` va `/hubs`). CORS: dev localhost, prod `Cors:AllowedOrigins`.

---

## 9. Ochiq savollar (keyin aniqlanadi)
1. Landing (Welcome) alohida marshrutmi (`/`) yoki alohida sayt? — hozircha `/` deb olamiz.
2. SuperAdmin konsoli shu SPA ichidami yoki alohida? — hozircha shu loyiha ichida `/_sa/...`.
3. Chek chop etish (invoice) — PDF endpoint bormi (`/Sales/{id}/invoice`) yoki front render? — endpoint bor, uni ishlatamiz.
4. Offline/PWA kerakmi (kassa uzilishlari uchun)? — MVP'da yo'q, keyinchalik.

---

### Keyingi qadam
Ushbu rejani tasdiqlang → **Bosqich 0 (Setup)** dan boshlaymiz: Vite scaffold, Tailwind token'lar,
folder struktura va birinchi ekran sifatida **Login**.
