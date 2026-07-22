# TZ — Buildix: Sub-path login va obuna (subscription) tizimi

**Holat:** Implement qilingan · **Sana:** 2026-07-19 · **Ko'lam:** backend .NET 9 (shu repo); frontend (Flutter) va ops/infra — alohida repo/boshqaruvda.

---

## Mundarija

1. [Kirish, maqsad, ko'lam va atamalar](#1-kirish-maqsad-kolam-va-atamalar)
2. [Domain modeli va obuna holati semantikasi](#2-domain-modeli-va-obuna-holati-semantikasi)
3. [Registratsiya / onboarding (faqat SuperAdmin)](#3-registratsiya--onboarding-faqat-superadmin)
4. [Login oqimi (sub-path) va silent auto-login](#4-login-oqimi-sub-path-va-silent-auto-login)
5. [Obuna nazorati (login + real-time) va xato kodlari](#5-obuna-nazorati-login--real-time-va-xato-kodlari)
6. [Public market-holati endpoint](#6-public-market-holati-endpoint)
7. [To'liq API kontrakt (o'zgargan / yangi endpointlar)](#7-toliq-api-kontrakt-ozgargan--yangi-endpointlar)
8. [Frontend (Flutter) kontrakti — alohida repo](#8-frontend-flutter-kontrakti--alohida-repo)
9. [Ops / infratuzilma (bu repoda emas)](#9-ops--infratuzilma-bu-repoda-emas)
10. [Xavfsizlik invariantlari](#10-xavfsizlik-invariantlari)
11. [Test va verifikatsiya](#11-test-va-verifikatsiya)

---

## 1. Kirish, maqsad, ko'lam va atamalar

### Kontekst (nega kerak)

Buildix — ko'p-ijarali (multi-tenant) ERP tizimi. Har bir biznes = bitta **Market** (tenant), va har bir marketning o'z **sub-path** slug'i bor: `buildix.uz/{subdomain}/login`. Routing haqiqiy DNS subdomain emas, **path (sub-path) asosida** — mavjud `Market.Subdomain` ustuni URL slug sifatida qayta ishlatiladi (wildcard DNS/TLS kerak emas).

Ushbu TZ ikki asosiy muammoni hal qiladi:

| Ilgari (muammo) | Endi (yechim) |
|---|---|
| Ochiq `POST /api/Auth/Register` adminni chetlab market ochardi | Ochiq self-register **olib tashlandi**; registratsiya faqat **SuperAdmin** orqali |
| Login `username` + `password` — market/slug bilan bog'lanmagan; cross-tenant username kolliziyasi | Login URL slug'iga cheklanadi → to'g'ri market ichida qidiriladi |
| `Market.ExpiresAt` (obuna muddati) **hech qayerda tekshirilmasdi** | Obuna login vaqtida **va** middleware'da real-time nazorat qilinadi |
| Login ekranini boshqaradigan ochiq holat endpoint'i yo'q edi | Yangi anonim `GET /api/public/market/{subdomain}` |

### Maqsadlar

1. **Registratsiyani markazlashtirish** — user adminga murojaat qiladi (`POST /api/RegistrationRequests`: `FullName` + `Phone`), SuperAdmin market nomi + username + password (+ ixtiyoriy `ExpiresAt`) kiritib tasdiqlaydi yoki qo'lda yaratadi; tizim slug (`Subdomain`) generatsiya qiladi.
2. **Sub-path bo'yicha kirish eshigi** — `buildix.uz/{slug}/` haqiqiy ERP kirish nuqtasi; slug path segmentidan olinadi va API'ga uzatiladi.
3. **Sodda login** — `POST /api/Auth/Login` `{ username, password, subdomain }`; slug berilsa login shu marketga cheklanadi va obuna eshigi tekshiriladi.
4. **Yagona obuna qoidasi** — `Market` entity ichida bitta manba; login, middleware va public endpoint shu bir qoidani chaqiradi (drift bo'lmaydi).
5. **Real-time enforcement** — bloklangan → `423 MARKET_BLOCKED`, muddati tugagan → `402 SUBSCRIPTION_EXPIRED`; faol sessiya keyingi so'rovdayoq to'xtaydi.
6. **Silent auto-login** — `AuthResponse` ga `marketId` + `subdomain` qo'shildi; klient saqlangan sessiyani path slug'iga solishtirib, tirik token bo'lsa login qilmasdan kiradi.
7. **Login ekranini boshqarish** — anonim `GET /api/public/market/{subdomain}` marketning `state` holatini beradi (`active` | `expired` | `blocked`).

### Ko'lam (scope)

**Shu repoda (backend, .NET 9 — TZ qamrovi):**

- `Market` domain qoidalari: `IsSubscriptionActive(nowUtc)`, `IsSubscriptionExpired(nowUtc)`.
- Login DTO + logika (`AuthService.Login.cs`), `AuthResponse` kengaytmasi.
- Real-time middleware enforcement (`TenantResolutionMiddleware.cs`).
- Yangi `SubscriptionExpiredException` + `GlobalExceptionHandlerMiddleware` xato JSON'i.
- Ochiq `PublicMarketController` + rate-limit.
- Ochiq self-register olib tashlanishi.
- Registratsiya DTO'lariga obuna muddati (`ExpiresAt`).
- Testlar (`Buildix.Tests`, xUnit + EF InMemory).

**Shu repodan tashqarida (kontrakt sifatida hujjatlanadi, kod bu yerda YO'Q):**

- **Frontend** — alohida Flutter SPA repo: routing, silent auto-login oqimi, global error-mapper.
- **Ops / infra** — nginx SPA fallback, CORS/`AllowedHosts`, DNS (config fayllari bu repoda yo'q).
- **Kelajak bosqichi** — onlayn to'lov (Payme/Click), obuna rejalari, avtomatik eslatmalar — ko'lamdan tashqarida (hozir obunani admin qo'lda boshqaradi).

### Atamalar lug'ati

| Atama | Ma'nosi |
|---|---|
| **Market / tenant** | Alohida biznes egasi (ijara birligi). `Buildix.Domain/Entities/Market.cs`; barcha ma'lumot `MarketId` bo'yicha ajratiladi. |
| **Subdomain / slug** | `Market.Subdomain` ustuni — unique, DNS-safe URL identifikatori (masalan `sardor-market`). Haqiqiy DNS subdomain **emas**. |
| **Sub-path** | Slug asosidagi path: `buildix.uz/{subdomain}/login`. Kirish eshigi; routing path segmentiga tayanadi. |
| **Obuna (subscription)** | `Market.ExpiresAt` bilan boshqariladigan kirish huquqi. `ExpiresAt == null` = grandfather (cheklovsiz/ochiq). |
| **`state`** | Public endpoint qaytaradigan market holati: `active` \| `expired` \| `blocked`. |
| **SuperAdmin** | `Role = 0` — barcha marketlarni boshqaradi; registratsiyani tasdiqlaydi/yaratadi. `MarketId == null` → obuna/block tekshiruvlaridan ozod. |
| **Owner** | `Role = 1` — faqat o'z marketini boshqaradi. |
| **Admin** | `Role = 2` — o'z marketida ma'lum huquqlar. |
| **Seller** | `Role = 3` — o'z marketida faqat sotuv. |

### Obuna holati (yagona qoida)

`Market` entity metodlari — barcha tekshiruvlar shu manbadan foydalanadi:

```
active   : !IsBlocked && IsActive && (ExpiresAt == null || ExpiresAt > nowUtc)    → IsSubscriptionActive(nowUtc)
expired  : IsActive && !IsBlocked && ExpiresAt != null && ExpiresAt <= nowUtc     → IsSubscriptionExpired(nowUtc) → 402 SUBSCRIPTION_EXPIRED
blocked  : IsBlocked                                                              → 423 MARKET_BLOCKED
inactive : !IsActive                                                              → 404 (soft-deleted)
```

Vaqt har doim UTC'da (`DateTime.UtcNow` / `ITashkentClock.UtcNow`). Xato JSON shakli barcha kanallar uchun yagona: `{ code, message, expiresAt | reason | blockedAt, statusCode }`.

### Holat: Implement qilingan

> **Ushbu TZ mavjud, ishlayotgan tizimni hujjatlashtiradi.** Barcha o'zgarishlar amalga oshirilgan: build toza, 34 test yashil (`dotnet test Buildix.Tests`). Yangi DB ustuni kerak emas — barcha maydonlar (`Subdomain`, `IsActive`, `ExpiresAt`, `IsBlocked`) allaqachon mavjud, faqat kod o'zgardi.

---

## 2. Domain modeli va obuna holati semantikasi

`Market` — bu Buildix'da bitta ijarachini (tenant) ifodalovchi asosiy domen entiti. Butun platformaning "kirish eshigi" (subdomain login, real-time middleware va public market-state endpoint) uning obuna holatiga tayanadi. Shu sababli obuna qoidasi **yagona manba** sifatida faqat `Market` entiti ichida — `IsSubscriptionActive` / `IsSubscriptionExpired` metodlarida — yashaydi va hech qachon login yo'li, middleware yoki endpoint'lar orasida "ikkilanib" ketmaydi.

Manba: `Buildix.Domain/Entities/Market.cs`.

### Market maydonlari

Quyidagi jadval obuna va tenant-holati semantikasi uchun ahamiyatli maydonlarni keltiradi (`Market` ning to'liq maydonlari emas — faqat holatga daxldorlari):

| Maydon | Tip | Default | Rol / semantika |
|---|---|---|---|
| `Id` | `int` | auto-increment | Primary key (int, GUID emas). |
| `Name` | `string` | `""` | Market (biznes) nomi. |
| `Subdomain` | `string?` | `null` | URL slug (`buildix.uz/{subdomain}/login`). Haqiqiy DNS subdomain emas — PATH asosidagi routing uchun ishlatiladi. |
| `Description` | `string?` | `null` | Ixtiyoriy tavsif. |
| `IsActive` | `bool` | `true` | **Soft-delete bayrog'i** (`DeleteOwner` uni `false` qiladi). `false` = market o'chirilgan (inactive) va tashqi dunyoga "topilmadi" (404) sifatida ko'rinadi. |
| `ExpiresAt` | `DateTime?` | `null` | Obuna tugash sanasi (UTC). `null` = muddat qo'yilmagan (grandfather / cheksiz). |
| `CreatedAt` | `DateTime` | `DateTime.UtcNow` | Yaratilgan vaqt (UTC). |
| `IsBlocked` | `bool` | `false` | **Operatsion, qaytariladigan** blok bayrog'i (odatda to'lov to'xtaganda). `IsActive`'dan ajratilgan: bloklangan market mavjud bo'lib qoladi va tiklanishi mumkin, lekin barcha autentifikatsiya va tenant-resolution urinishlarini rad etadi. |
| `BlockedAt` | `DateTime?` | `null` | Blok qo'yilgan vaqt (meta, klientga uzatiladi). |
| `BlockedReason` | `string?` | `null` | Blok sababi (meta, klientga `reason` sifatida uzatiladi). |
| `BlockedByUserId` | `Guid?` | `null` | Blokni qo'ygan foydalanuvchi (audit meta). |
| `OwnerId` | `Guid` | — | Marketni yaratgan ega(owner). |

> **`IsActive` va `IsBlocked` farqi.** `IsActive` — soft-delete (o'chirilgan / mavjud emas), `IsBlocked` — operatsion, qaytariladigan cheklov (mavjud, lekin kirish yopilgan). Ikkalasi turli "eshik"lar orqali boshqariladi va turli status kod beradi.

### Obuna qoidasi metodlari

Obuna qoidasi entiti ichidagi ikki `pure` metod bilan ifodalanadi. Vaqt **doimo UTC** — `DateTime.UtcNow` / `ITashkentClock.UtcNow` ga solishtiriladi, `nowUtc` argument sifatida uzatiladi.

```csharp
// Login eshigi ochiq: bloklanmagan, o'chirilmagan va
// (muddat qo'yilmagan yoki muddat hali kelajakda).
public bool IsSubscriptionActive(DateTime nowUtc) =>
    IsActive && !IsBlocked && (!ExpiresAt.HasValue || ExpiresAt.Value > nowUtc);

// Obunaning belgilangan tugash sanasi o'tib ketgan.
// Bloklangan/o'chirilgan marketlar o'z eshiklari orqali hal bo'lgani uchun
// bu yerda false qaytadi (expired-but-blocked → "blocked", "expired" emas).
public bool IsSubscriptionExpired(DateTime nowUtc) =>
    IsActive && !IsBlocked && ExpiresAt.HasValue && ExpiresAt.Value <= nowUtc;
```

Aniq shartlar:

- **`IsSubscriptionActive(nowUtc)`** → `true` bo'lishi uchun: `IsActive == true` **VA** `IsBlocked == false` **VA** (`ExpiresAt == null` **YOKI** `ExpiresAt > nowUtc`).
- **`IsSubscriptionExpired(nowUtc)`** → `true` bo'lishi uchun: `IsActive == true` **VA** `IsBlocked == false` **VA** `ExpiresAt != null` **VA** `ExpiresAt <= nowUtc`.

E'tibor bering: ikkala metod ham `IsActive && !IsBlocked` sharti bilan boshlanadi. Ya'ni:

- Bloklangan market uchun **ikkala metod ham `false`** qaytaradi — u "expired" emas, "blocked" sifatida yuzaga chiqadi (`IsBlocked` ustuvor).
- Soft-delete qilingan (`!IsActive`) market uchun ham ikkala metod `false` — u "topilmadi" (404) sifatida ko'rinadi.
- Chegara nuqtasi: `ExpiresAt == nowUtc` bo'lsa (`<=`), market **expired** hisoblanadi (active emas). Ya'ni tugash lahzasi kirmaydi.

### ExpiresAt == null → grandfather semantikasi

`ExpiresAt == null` = "muddat qo'yilmagan" (grandfathered / cheksiz). Bunday market **hech qachon expired bo'lmaydi**:

- `IsSubscriptionExpired` da `ExpiresAt.HasValue == false` → to'g'ridan-to'g'ri `false`.
- `IsSubscriptionActive` da `!ExpiresAt.HasValue == true` → muddat sharti avtomatik qanoatlantiriladi va (blok/soft-delete bo'lmasa) `true`.

Bu grandfather rejimi: eski yoki cheksiz obunali marketlar `ExpiresAt = null` bilan doimo ochiq qoladi. Obunani muddatli qilish uchun admin `ExpiresAt` ga kelajak sanani qo'yadi.

### Holat matritsasi

Har bir market istalgan lahzada aynan bitta mantiqiy holatda bo'ladi. Ustuvorlik: **blocked → inactive/soft-delete → expired → active**. Public `GET /api/public/market/{subdomain}` endpoint faqat `active | expired | blocked` `state` qiymatlarini qaytaradi; soft-deleted / noma'lum market esa **404** (endpoint natijasida `state` ko'rinmaydi).

| Holat | Aniqlovchi shart | `IsSubscriptionActive` | `IsSubscriptionExpired` | HTTP (enforcement) | Exception / natija |
|---|---|---|---|---|---|
| **active** | `!IsBlocked && IsActive && (ExpiresAt == null \|\| ExpiresAt > now)` | `true` | `false` | 200 (kirish ochiq) | — |
| **expired** | `IsActive && !IsBlocked && ExpiresAt != null && ExpiresAt <= now` | `false` | `true` | **402** `SUBSCRIPTION_EXPIRED` | `SubscriptionExpiredException` |
| **blocked** | `IsBlocked` (boshqa maydonlardan qat'i nazar) | `false` | `false` | **423** `MARKET_BLOCKED` | `MarketBlockedException` |
| **inactive** | `!IsActive` (soft-deleted) | `false` | `false` | **404** (topilmadi) | Public endpoint 404; login topilmadi |

### Domen exception'lari va xato modeli

Enforcement (login vaqtida va middleware'da real-time) yuqoridagi holatlarni `DomainException` avlodlariga xaritalaydi. Baza `DomainException` har bir xatoning `StatusCode`, `Code`, `UserMessage` va ixtiyoriy meta maydonlarini (`BlockedAt`, `ExpiresAt`, `Reason`) o'zida saqlaydi — global exception handler ularni bitta `case DomainException` armi bilan JSON'ga o'giradi, per-tip switch yo'q.

Manba: `Buildix.Domain/Exceptions/DomainException.cs`, `SubscriptionExpiredException.cs`, `MarketBlockedException.cs`.

| Exception | `StatusCode` | `Code` | Meta maydon | Baza (internal) message |
|---|---|---|---|---|
| `SubscriptionExpiredException` | `402` | `SUBSCRIPTION_EXPIRED` | `ExpiresAt` (obuna tugash sanasi) | `Market {marketId} subscription expired at {expiresAt:O}` |
| `MarketBlockedException` | `423` | `MARKET_BLOCKED` | `Reason`, `BlockedAt` | `Market {marketId} is blocked: {reason}` |

`DomainException` bazasidagi umumiy kontrakt:

- `int StatusCode` — API qaytaradigan HTTP status (masalan 402, 423).
- `string Code` — klient shoxlaydigan barqaror, mashina o'qiy oladigan kod.
- `string UserMessage` — lokalizatsiyalangan, klientga xavfsiz xabar (internal detallar sizib chiqmaydi). Baza `Exception.Message` esa faqat loglar uchun ichki (inglizcha) diagnostika bo'lib qoladi.
- Ixtiyoriy `DateTime? BlockedAt`, `DateTime? ExpiresAt`, `string? Reason` — default `null`, tegishli avlod override qiladi.

Shu maydonlar mijozga yuboriladigan xato JSON'ini shakllantiradi: `{ code, message, expiresAt | reason | blockedAt, statusCode }`. Masalan, `SubscriptionExpiredException` `expiresAt` bilan (klient "obuna tugadi — yangilang" ekranini ko'rsatadi), `MarketBlockedException` esa `reason` + `blockedAt` bilan (klient "administrator bilan bog'laning" ekranini ko'rsatadi) uzatiladi.

---

## 3. Registratsiya / onboarding (faqat SuperAdmin)

Buildix'da yangi biznes (tenant) ochish yagona nazorat nuqtasidan — **SuperAdmin** orqali o'tadi. Ochiq self-register butunlay olib tashlangan: tashqi foydalanuvchi o'zicha akkaunt va Market yarata olmaydi. Uning o'rniga ikki bosqichli onboarding: (1) tashrifchi qisqa **murojaat** qoldiradi, (2) SuperAdmin uni ko'rib chiqib **tasdiqlaydi** yoki umuman murojaatsiz **qo'lda** Owner yaratadi. Har ikkala yo'l ham bir xil natijaga olib keladi: `Owner` (User) + `Market` + `CashRegister` + generatsiya qilingan `subdomain`.

### Umumiy oqim

```
Anonim tashrifchi                SuperAdmin konsoli               Tizim (atomik tranzaksiya)
─────────────────                ──────────────────               ──────────────────────────
POST /api/RegistrationRequests
  { fullName, phone }
        │
        ▼
  Pending so'rov  ──────────────► GET  .../requests?status=Pending
                                  POST .../requests/{id}/approve ──► User(Owner) + Market
                                       { username, password,          + CashRegister + subdomain
                                         marketName, subdomain?,       → request = Approved
                                         language?, expiresAt? }
                                  ── yoki ──
                                  POST .../owners  (murojaatsiz) ──► xuddi shu natija
                                       { fullName, phone,
                                         username, password,
                                         marketName, subdomain?,
                                         language?, expiresAt? }
```

### Endpoint marshrutlari

`{seg}` — `SuperAdmin:ConsoleSegment` konfiguratsiyasidan olinadigan yashirin (opaque) segment. Noto'g'ri segment bilan kelgan so'rov `SuperAdminPathGateMiddleware` tomonidan autentifikatsiyadan **oldin** 404 qaytaradi — skaner konsol borligini ham bilmaydi. Asosiy himoya esa baribir JWT `SuperAdmin` roli.

| Metod & marshrut | Kirish | DTO (body) | Vazifasi |
|---|---|---|---|
| `POST /api/RegistrationRequests` | Anonim (`[AllowAnonymous]`, rate-limit `registration-submit`) | `SubmitRegistrationRequestDto` | Murojaat qoldirish |
| `GET /api/_sa/{seg}/requests?status=` | SuperAdmin | — | Murojaatlar ro'yxati (status bo'yicha filtr) |
| `GET /api/_sa/{seg}/check-availability` | SuperAdmin | — (query: `username`, `marketName`, `subdomain`) | Real-time bandlik tekshiruvi + `suggestedSubdomain` |
| `POST /api/_sa/{seg}/requests/{id:guid}/approve` | SuperAdmin | `ApproveRegistrationRequestDto` | Murojaatni tasdiqlab, Owner+Market yaratish |
| `POST /api/_sa/{seg}/requests/{id:guid}/reject` | SuperAdmin | `RejectRegistrationRequestDto` | Murojaatni rad etish (sabab majburiy) |
| `POST /api/_sa/{seg}/owners` | SuperAdmin | `CreateOwnerDto` | Murojaatsiz qo'lda Owner+Market yaratish |

> Eslatma: `GET/PUT/DELETE /api/_sa/{seg}/owners/{id}` va `markets/{marketId}/block|unblock` ham shu konsolda, lekin ular onboarding'dan keyingi boshqaruvga tegishli (alohida bo'limlarda).

### 1-bosqich — Murojaat (`SubmitRegistrationRequest`)

`RegistrationRequestsController` da **faqat bitta** POST bor — ataylab `GET/PUT/DELETE` yo'q, shunda navbat (queue) yopiq qoladi va telefon raqamlar sizib chiqmaydi.

So'rov tanasi (`SubmitRegistrationRequestDto`) — atigi ikki maydon:

```json
{ "fullName": "Sardor Aliyev", "phone": "+998901234567" }
```

**Javob siyosati (enumeratsiyaga qarshi):** telefon navbatda bor-yo'qligini yoki formati noto'g'riligini oshkor qilmaslik uchun deyarli barcha holatda **bir xil `200 OK`** qaytariladi:

```json
{ "message": "Adminga yubordik. Admin tez orada javob beradi." }
```

Faqat servis xatoni `RegistrationSubmitCodes.Validation` deb belgilagan holatda (format bo'yicha maslahat) `400 Bad Request` qaytadi va `message` maydonida foydalanuvchiga ko'rsatiladigan izoh bo'ladi. Boshqa barcha sabab (masalan, dublikat telefon, shubhali shakl) server logiga yoziladi va yuqoridagi umumiy `200` ortiga yashiriladi. Yagona tashqi 4xx — rate-limiter'dan keladigan `429`.

### 2-bosqich — Tasdiqlash (`approve`) va qo'lda yaratish (`owners`)

Ikkala endpoint ham bir xil yadro logikaga ega; farqi — `approve` mavjud `Pending` murojaatdan foydalanadi va uni `Approved` ga o'tkazadi, `owners` esa murojaatsiz to'g'ridan-to'g'ri yaratadi.

#### So'rov tanasi

`ApproveRegistrationRequestDto` (FullName/Phone murojaatdan olinadi):

```json
{
  "username": "sardor",
  "password": "StrongPass1",
  "marketName": "Sardor Market",
  "subdomain": "sardor-market",
  "language": "uz",
  "expiresAt": "2026-12-31T23:59:59Z"
}
```

`CreateOwnerDto` — yuqoridagining ustiga murojaat bermagan `fullName` + `phone` maydonlarini qo'shadi:

```json
{
  "fullName": "Sardor Aliyev",
  "phone": "+998901234567",
  "username": "sardor",
  "password": "StrongPass1",
  "marketName": "Sardor Market",
  "subdomain": null,
  "language": "uz",
  "expiresAt": null
}
```

**`expiresAt` (obuna semantikasi):** ikkala DTO'ga ham `expiresAt` qo'shilgan va u to'g'ridan-to'g'ri `Market.ExpiresAt` ga yoziladi. Bu — obuna eshigining yagona manbasi. `null` bo'lsa — muddat belgilanmagan (grandfather / ochiq); admin keyinchalik `UpdateOwner` orqali uzaytiradi. Qiymat berilsa, sub-path login shu vaqtgacha ochiq, so'ng avtomatik `402 SUBSCRIPTION_EXPIRED`.

#### Validatsiya qoidalari

| Maydon | Qoida | Xato (400 `message`) |
|---|---|---|
| `username` | `NormalizeUsername`: trim + lowercase, ≥ 3 belgi | "Username kamida 3 ta belgidan iborat bo'lsin." |
| `password` | `StrongPassword`, ≥ 8 belgi | "Parol kamida 8 ta belgidan iborat bo'lsin." |
| `marketName` | trim, ≥ 3 belgi | "Do'kon nomini kiriting (kamida 3 belgi)." |
| `fullName` (faqat `owners`) | trim, ≥ 2 belgi | "Ism va familiyani kiriting." |
| `phone` (faqat `owners`) | `NormalizePhone` → `+998XXXXXXXXX` (9 / 12(`998…`) / 14(`00998…`) raqam qabul qilinadi) | "Telefon raqami formati noto'g'ri. Misol: +998 90 123 45 67." |
| `language` | `"uz"` → Uzbek, `"ru"` → Russian, aks holda Uzbek | — |
| `subdomain` | bo'sh → generatsiya; aks holda `ValidateAndNormalizeSubdomain` | quyida |

Username lowercase saqlanadi, chunki PostgreSQL `=` katta-kichik harfga sezgir — bu `"Sardor"`, `" sardor"`, `"sardor"` ni bitta akkaunt qilib birlashtiradi va login so'rovining nondeterministik bo'lishining oldini oladi.

#### Subdomain generatsiya va validatsiya

Subdomain URL slug'i sifatida ishlatiladi (haqiqiy DNS emas), lekin DNS-xavfsiz formatda saqlanadi:

- **`GenerateSubdomain(username)`** — `subdomain` bo'sh bo'lsa chaqiriladi. Username'dan faqat harf/raqamlarni oladi (bo'sh chiqsa `"market"`), oxiriga `Guid` dan 6 ta hex belgi qo'shadi. Masalan `sardor` → `sardor3f9a1c`. Bu deyarli har doim noyob slug beradi.
- **`ValidateAndNormalizeSubdomain(raw)`** — foydalanuvchi bergan qiymatni trim + lowercase qiladi va quyidagi regex bo'yicha tekshiradi:

```
^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])?$        // 3–63 belgi, lotin harf/raqam/'-'
```

Faqat lotin harflari, raqamlar va `-` ruxsat etiladi; boshi va oxiri alfanumerik bo'lishi shart (nuqta, `_`, chetdagi `-` rad etiladi — host header / sertifikat qidiruvini buzadi). Xato: "Subdomain noto'g'ri formatda. Faqat lotin harflari, raqamlar va '-' (3-63 belgi)."

**Noyoblik (uniqueness):** SuperAdmin konsolidagi `GET .../check-availability` real vaqtda `usernameAvailable` / `marketNameAvailable` / `subdomainAvailable` (har biri `true`/`false`/`null` — so'ralmagan bo'lsa `null`) va `suggestedSubdomain` qaytaradi (`CheckAvailabilityResultDto`) — forma to'ldirilayotganda indikator sifatida.

#### Yaratish tranzaksiyasi (atomik)

Yaratish `CreateExecutionStrategy` ortidagi yagona tranzaksiyada bajariladi. Ketma-ketlik:

1. `LoadRequestForUpdateAsync` — murojaat qatorini tranzaksiya ichida **qayta o'qiydi** (PostgreSQL'da `SELECT … FOR UPDATE`, InMemory test provayderida oddiy o'qish + `xmin` konfliktini ushlash). `approve` uchun holat `Pending` bo'lishi shart; aks holda "So'rov allaqachon ko'rib chiqilgan (...)".
2. **Ilova darajasidagi noyoblik tekshiruvlari:** `username` band emasmi, `marketName` (katta-kichik harfga befarq, `MarketNameTakenAsync`) va `subdomain` band emasmi. Bandlik → 400 (masalan "'sardor' allaqachon ishlatilgan.").
3. `User` yaratiladi: `Role = Owner`, `IsActive = true`, dastlab `MarketId = null`, parol BCrypt bilan xeshlanadi. SaveChanges.
4. `Market` yaratiladi: `Name`, `Subdomain`, `IsActive = true`, `ExpiresAt = dto.ExpiresAt`, `OwnerId = userId`. SaveChanges.
5. `CashRegister` qo'shiladi (`CurrentBalance = 0`).
6. `user.MarketId = market.Id` (endi Owner o'z Marketiga bog'lanadi).
7. Faqat `approve`'da: murojaat `Approved` ga o'tadi — `ProcessedAt`, `ProcessedByUserId`, `CreatedUserId`, `CreatedMarketId` to'ldiriladi.
8. Commit → structured log + audit log (`approve`: `RegistrationRequest`/`Approved`; `owners`: `Owner`/`CreatedManually`).

**Poyga (race) himoyasi:** `AnyAsync` tekshiruvi bilan `INSERT` orasida parallel yaratish sirg'alib o'tsa, DB unique constraint (`SQLSTATE 23505`) ishga tushadi; `IsUniqueViolation` uni ushlab, rollback qilib, toza `400` qaytaradi: "Username, do'kon nomi yoki subdomain allaqachon band. Iltimos, qayta tekshiring." — operator 500 o'rniga tushunarli xabar oladi.

#### Javob (`ApproveRegistrationResultDto`)

Ikkala endpoint ham bir xil natija qaytaradi. Parol bu yerda **qaytarilmaydi** va hech qayerda saqlanmaydi — SuperAdmin uni operatorga tashqi kanal (SMS / qo'ng'iroq) orqali yetkazadi.

```json
{
  "requestId": "3f...b2",   // qo'lda yaratishda (owners) → Guid.Empty
  "userId": "a1...9c",
  "username": "sardor",
  "marketId": 42,
  "marketName": "Sardor Market"
}
```

Status kodlar: muvaffaqiyat `200 OK`; murojaat topilmasa `404` ("So'rov topilmadi."); validatsiya yoki bandlik `400` (yuqoridagi `message` lar); JWT'da `SuperAdmin` roli yo'q bo'lsa `403`, caller ID o'qib bo'lmasa `401`.

### Ochiq self-register olib tashlangani

Avvalgi anonim `POST /api/Auth/Register` endpointi **butunlay o'chirilgan**. Endi yangi tenant yaratishning yagona yo'li — SuperAdmin `approve`/`owners` orqali. Tashqi foydalanuvchiga qolgan yagona ochiq nuqta — `POST /api/RegistrationRequests` (faqat `fullName` + `phone`), u hech qanday akkaunt yoki Market yaratmaydi, faqat SuperAdmin ko'rib chiqadigan murojaat qoldiradi. Shu tariqa tenant yaratish, subdomain generatsiyasi va obuna muddatini (`expiresAt`) belgilash to'liq SuperAdmin nazoratida qoladi.

---

## 4. Login oqimi (sub-path) va silent auto-login

Buildix'da login **path asosidagi** ko'p-ijarali oqim bilan ishlaydi: foydalanuvchi `buildix.uz/{subdomain}/login` manzilidan kiradi. Bu yerda `{subdomain}` — haqiqiy DNS subdomain emas, balki URL slug (`Market.Subdomain` ustuni). Endpoint marshruti `AuthController`'da `[Route("api/[controller]/[action]")]` orqali aniqlanadi, ya'ni login manzili — `POST /api/Auth/Login`.

### LoginRequest DTO

Klient login formasidagi maydonlarni JSON tanaga joylaydi. `subdomain` — **ixtiyoriy**: mavjud bo'lsa login shu bitta marketga cheklanadi, bo'lmasa login market-agnostik qoladi (SuperAdmin root'dan slugsiz kiradi).

| Maydon (JSON) | Tur | Majburiy | Validatsiya | Vazifa |
| --- | --- | --- | --- | --- |
| `username` | `string` | ha | 3–50 belgi | Foydalanuvchi nomi |
| `password` | `string` | ha | 1–100 belgi | Parol (login'da **kuchli-parol** qoidasi tekshirilmaydi — eski akkauntlar kirishi uchun) |
| `subdomain` | `string?` | yo'q | — | URL'dan olingan market slug'i |

```jsonc
// POST /api/Auth/Login
{
  "username": "kassir01",
  "password": "••••••••",
  "subdomain": "bozor-market"   // buildix.uz/bozor-market/login dan olingan
}
```

### AuthResponse DTO

Muvaffaqiyatli login `AuthResponse` qaytaradi. Silent auto-login'ni ta'minlash uchun unga ikkita yangi maydon — `marketId` va `subdomain` — qo'shildi. SuperAdmin uchun ikkalasi ham `null` (marketga bog'lanmagan).

| Maydon (JSON) | Tur | Izoh |
| --- | --- | --- |
| `userId` | `Guid` | Foydalanuvchi ID |
| `username` | `string` | Login |
| `fullName` | `string` | To'liq ism |
| `role` | `string` | Rol (`Owner`, `Admin`, `Seller`, `SuperAdmin`) |
| `language` | `string` | `uz` yoki `ru` |
| `accessToken` | `string` | JWT access token |
| `refreshToken` | `string` | Refresh token (plaintext; DB'da faqat SHA-256 hash saqlanadi) |
| `expiresAt` | `DateTime` | Access token amal muddati |
| `permissions` | `string[]` | Effektiv ruxsatlar to'plami (klient UI'ni cheklaydi) |
| **`marketId`** | `int?` | **Sessiya tegishli tenant ID (SuperAdmin → `null`)** |
| **`subdomain`** | `string?` | **Tenant slug'i (SuperAdmin → `null`)** |

`subdomain` maydoni `GenerateAuthResponseAsync` ichida `user.MarketId` bo'yicha bitta ustunli (`Markets.Subdomain`) arzon `AsNoTracking()` so'rovdan olinadi; `MarketId == null` bo'lsa (SuperAdmin) so'rov o'tkazib yuboriladi.

### Server tomonlama login mantiqi (`AuthService.LoginAsync`)

Oqim quyidagi tartibda ketadi:

1. **Brute-force lock tekshiruvi** — DB'ga tegishdan oldin `_loginAttempts.GetLockedUntilUtc(username)` ko'riladi. Agar akkaunt allaqachon bloklangan bo'lsa, `LoginLockedException` otiladi (parol hash'i vaqtini isrof qilmaslik uchun).
2. **Slug bo'yicha marketni topish** — `subdomain` bo'lsa, u `Trim().ToLowerInvariant()` qilinib normallashtiriladi va `Markets` jadvalidan qidiriladi:
   - Market topilmasa **yoki** `!IsActive` bo'lsa → umumiy muvaffaqiyatsizlik qaytariladi (`RejectAndMaybeLockAsync`). Bu yerda "noto'g'ri slug = noto'g'ri eshik" tamoyili: qaysi marketlar borligini oshkor qilmaslik uchun xuddi noto'g'ri parol kabi javob beriladi.
   - Aks holda `EnsureMarketOpen(slugMarket)` — obuna eshigi (block/expiry) **kredensiallardan oldin** tekshiriladi (bu holatlar allaqachon public market-state endpoint orqali ochiq).
3. **Kandidatlarni cheklash** — foydalanuvchi qidiruvi slug bor-yo'qligiga qarab ikki xil:

   | Holat | Qidiruv sharti |
   | --- | --- |
   | Slug **bor** | `Username == request.Username && IsActive && MarketId == slugMarket.Id` |
   | Slug **yo'q** | `Username == request.Username && IsActive` (global) |

   Slug berilganda qidiruv bitta marketga cheklanadi — bu **cross-tenant username kolliziyasini** hal qiladi (bir xil `username` turli marketlarda bo'lishi mumkin).
4. **Timing-attack himoyasi** — kandidat topilmasa ham bitta `BCrypt.Verify` sobit dummy hash'ga (`DummyBcryptHash`) qarshi bajariladi, shunda "yo'q username" va "noto'g'ri parol" yo'llari deyarli teng vaqt sarflaydi (username enumeration'ni oldini oladi).
5. **Parol tekshiruvi** — kandidatlar bo'ylab birinchi `BCrypt.Verify` mos kelganida short-circuit qilinadi (`matched`). Bir nechta kandidat bo'lsa faqat ogohlantirish log'i yoziladi.
6. **No-slug yo'li uchun market eshigi** — `slugMarket == null && matched.MarketId != null` bo'lsa, `matched` foydalanuvchining marketi qayta yuklanib `EnsureMarketOpen(market)` chaqiriladi. Slug yo'li buni allaqachon oldindan tekshirgani uchun u yerda takrorlanmaydi.
7. **SuperAdmin bypass** — `matched.MarketId == null` (SuperAdmin) obuna eshigidan o'tmaydi; hamma tenant yopiq bo'lsa ham konsolga kira oladi.
8. **Shift eshigi** — `Seller` roli uchun `IsShiftActiveNow()` tekshiriladi; smena faol bo'lmasa `InvalidOperationException` otiladi va controller uni **400** sifatida foydalanuvchiga tushunarli xabar bilan qaytaradi. Owner/Admin/SuperAdmin'da smena yo'q.
9. **Muvaffaqiyat** — brute-force hisoblagichi tozalanadi, audit log yoziladi va `GenerateAuthResponseAsync` orqali `AuthResponse` (yangi `marketId` + `subdomain` bilan) qaytariladi.

#### `EnsureMarketOpen` — obuna eshigi

Ushbu yordamchi metod ikki holatda istisno otadi (block > expiry ustuvorligi bilan):

| Holat | Istisno | Status | JSON body maydonlari |
| --- | --- | --- | --- |
| `IsBlocked` | `MarketBlockedException` | **423** `MARKET_BLOCKED` | `code`, `message`, `reason`, `blockedAt`, `statusCode` |
| `IsSubscriptionExpired(now)` | `SubscriptionExpiredException` | **402** `SUBSCRIPTION_EXPIRED` | `code`, `message`, `expiresAt`, `statusCode` |

Bu istisnolarni global exception handler HTTP javobga aylantiradi; body'dagi `reason`/`expiresAt` klientga to'g'ri ekranni ko'rsatishga yordam beradi.

### Login javob kodlari

| Vaziyat | HTTP | Body |
| --- | --- | --- |
| Muvaffaqiyat | `200 OK` | `AuthResponse` |
| Noto'g'ri kredensial / noma'lum slug / inactive user | `401 Unauthorized` | `"Invalid credentials"` |
| Smena faol emas (Seller) | `400 Bad Request` | `{ "message": "Ish smenangiz hozir faol emas..." }` |
| Market bloklangan | `423` | `{ code: "MARKET_BLOCKED", ... }` |
| Obuna muddati tugagan | `402` | `{ code: "SUBSCRIPTION_EXPIRED", ... }` |
| Akkaunt qulflangan (brute-force) | `LoginLockedException` → "N daqiqadan keyin urinib ko'ring" | — |

Login endpoint `auth-login` rate-limiter bilan himoyalangan (`[EnableRateLimiting("auth-login")]`).

### Silent auto-login (klient tomonlama)

`AuthResponse`'dagi `marketId` + `subdomain` klientga saqlangan sessiyani URL path slug'iga solishtirib, **login qilmasdan** kirish imkonini beradi. Bu logika Flutter klientida (alohida repo) yashaydi; server faqat kerakli maydonlarni va endpoint'larni taqdim etadi:

1. Foydalanuvchi `buildix.uz/{subdomain}/` ni ochadi; klient URL'dan slug'ni ajratib oladi.
2. Klient saqlangan sessiyaning `subdomain` maydonini URL slug'iga solishtiradi.
   - **Mos kelmasa** (boshqa market yoki sessiya yo'q) → login ekrani ko'rsatiladi.
   - **Mos kelsa** → tirik token tekshiruviga o'tiladi.
3. Klient `GET /api/Users/MyProfile` ni saqlangan access token bilan chaqiradi:
   - `200 OK` → token tirik; login formasi ko'rsatilmasdan to'g'ridan-to'g'ri kiritiladi (silent auto-enter).
   - `401 Unauthorized` → access token muddati tugagan; keyingi qadamga o'tiladi.
4. `POST /api/Auth/RefreshToken` orqali `accessToken` + `refreshToken` juftligi yuboriladi (`auth-refresh` rate-limiter):
   - `200 OK` → yangi juftlik olinadi (rotatsiya) va foydalanuvchi login qilmasdan kiritiladi.
   - `401 Unauthorized` → refresh ham yaroqsiz; login ekrani ko'rsatiladi.

```text
buildix.uz/{subdomain}/  ochildi
        │
        ▼
saqlangan session.subdomain == URL slug ?
        │ yo'q ───────────────► LOGIN ekrani
        │ ha
        ▼
GET /api/Users/MyProfile (access token bilan)
        │ 200 ─────────────────► SILENT ENTER
        │ 401
        ▼
POST /api/Auth/RefreshToken (accessToken + refreshToken)
        │ 200 ─► yangi juftlik ─► SILENT ENTER
        │ 401 ─────────────────► LOGIN ekrani
```

> **Diqqat:** `RefreshToken` javobida ham 401 yagona umumiy javob sifatida qaytadi — sabab (noma'lum/muddati o'tgan/begona/o'g'irlangan) oshkor qilinmaydi. Rotatsiya poygasi (ikki tab) va "javob yo'lda yo'qoldi" holatlari `AuthService` ichidagi grace oynasi orqali hal qilinadi, shuning uchun controller'da alohida 409 shoxi yo'q.

Bu mexanizm obuna qoidasini ham hurmat qiladi: middleware har so'rovda real-time enforcement qilgani uchun, silent auto-login vaqtida market bloklangan/muddati tugagan bo'lsa, `MyProfile` yoki keyingi so'rovlar mos ravishda **423** yoki **402** qaytaradi va klient to'g'ri ekranga yo'naltiradi.

---

## 5. Obuna nazorati (login + real-time) va xato kodlari

Buildix marketning obuna "eshigini" (subscription door) **ikki mustaqil nuqtada** majburlaydi (enforcement). Ikkalasi ham obuna qoidasini bitta yagona manbadan — `Market` entity metodlaridan (`Market.IsBlocked`, `Market.IsSubscriptionExpired(now)`) — o'qiydi, shuning uchun login yo'li, middleware va public state endpoint hech qachon bir-biridan farq qilmaydi.

- **Nuqta 1 — Login vaqti (proaktiv):** foydalanuvchi hali ichkariga kirmasdan turib, `AuthService.EnsureMarketOpen(...)` eshikni tekshiradi va yopiq bo'lsa domain exception otadi (throw).
- **Nuqta 2 — Har bir so'rov (real-time):** `TenantResolutionMiddleware` allaqachon berilgan (issued) tirik token bilan kelgan har bir so'rovda eshikni qayta tekshiradi. Bu SuperAdmin bloklashi yoki muddat tugashi **keyingi so'rovdayoq** kuchga kirishini kafolatlaydi — 30 daqiqalik access token muddati tugashini kutmasdan.

### Nuqta 1: Login vaqtidagi enforcement (`AuthService.EnsureMarketOpen`)

`EnsureMarketOpen(Market market)` — `AuthService.Login.cs` ichidagi private metod. U ikki holatda chaqiriladi:

1. **Slug yo'li:** login URL'idan (`buildix.uz/{subdomain}/login`) slug kelganda, kredensiallarga (parol) tegishdan **oldin** — `slugMarket` topilib, `slugMarket.IsActive` bo'lsa. Bu ataylab parol tekshiruvidan oldin turadi, chunki blok/muddat holati tenant darajasidagi ma'lumot va u public state endpoint orqali baribir ochiq.
2. **Slug'siz yo'l (SuperAdmin / market-agnostik login):** parol mos kelgan (`matched`) foydalanuvchining `MarketId` bo'lsa, market qayta o'qilib `EnsureMarketOpen` chaqiriladi. **SuperAdmin** (`MarketId == null`) bu tekshiruvni butunlay chetlab o'tadi — barcha tenantlar yopiq bo'lsa ham konsolga kira oladi.

Metod ichidagi ustuvorlik (precedence) — **blok muddatdan ustun**:

```csharp
private void EnsureMarketOpen(Market market)
{
    if (market.IsBlocked)
        throw new MarketBlockedException(market.Id, market.BlockedReason, market.BlockedAt);

    if (market.IsSubscriptionExpired(DateTime.UtcNow))
        throw new SubscriptionExpiredException(market.Id, market.ExpiresAt);
}
```

Ya'ni market bir vaqtning o'zida ham bloklangan, ham muddati tugagan bo'lsa — mijozga **`MARKET_BLOCKED` (423)** qaytadi.

> Diqqat: slug topilmasa yoki `IsActive == false` bo'lsa, `EnsureMarketOpen` umuman chaqirilmaydi — buning o'rniga login noto'g'ri parol bilan bir xil generik javob (`RejectAndMaybeLockAsync`) qaytaradi, toki endpoint qaysi marketlar mavjudligini oshkor qilmasin.

### DomainException → HTTP mapping (`GlobalExceptionHandlerMiddleware`)

Login yo'li exception otadi; javob shaklini **bitta arm** — `GlobalExceptionHandlerMiddleware` ичidagi `case DomainException domainEx` — quradi. Har bir domain exception o'zining HTTP statusini, mashina o'qiy oladigan `Code`'ini, mijozga xavfsiz `UserMessage`'ini va (ixtiyoriy) `Reason` / `BlockedAt` / `ExpiresAt` maydonlarini **o'zida** olib yuradi. Yangi domain exception qo'shish middleware'ni tahrirlashni talab qilmaydi.

```csharp
case DomainException domainEx:
    context.Response.StatusCode = domainEx.StatusCode;   // 423 / 402 / ...
    response.Message           = domainEx.UserMessage;
    response.Code              = domainEx.Code;           // "MARKET_BLOCKED" / ...
    response.Reason            = domainEx.Reason;
    response.BlockedAt         = domainEx.BlockedAt;
    response.ExpiresAt         = domainEx.ExpiresAt;
    break;
```

Domain exception → HTTP status jadvali (obuna bilan bog'liqlari qalin):

| Exception | Code | HTTP status |
|---|---|---|
| **`MarketBlockedException`** | **`MARKET_BLOCKED`** | **423 Locked** |
| **`SubscriptionExpiredException`** | **`SUBSCRIPTION_EXPIRED`** | **402 Payment Required** |
| `LoginLockedException` | `ACCOUNT_LOCKED` | 429 Too Many Requests |
| `ShiftNotOpenException` | `SHIFT_NOT_OPEN` | 409 Conflict |
| `DuplicateUsernameException` | `USERNAME_TAKEN` | 409 Conflict |

Javob JSON `camelCase` bilan seriyalanadi va `null` maydonlar tushirib qoldiriladi (`WhenWritingNull`), shu bois `MARKET_BLOCKED` javobida `expiresAt`, `SUBSCRIPTION_EXPIRED` javobida esa `reason`/`blockedAt` ko'rinmaydi. Har bir javobda `traceId` va `statusCode` ham bo'ladi.

### Nuqta 2: Real-time middleware enforcement (`TenantResolutionMiddleware`)

`TenantResolutionMiddleware` autentifikatsiyadan keyin ishlaydi. U quyidagilarni **tashlab ketadi** (skip): `skipPaths` ro'yxatidagi yo'llar (`/api/Auth/Login`, `/api/_sa/`, `/api/RegistrationRequests`, `/api/public/`, `/health`, `/hubs` va h.k.), autentifikatsiyalanmagan so'rovlar, hamda **SuperAdmin** (roli `SuperAdmin` — JWT'da `MarketId` claim yo'q, cross-tenant).

Qolgan hollarda tenant faqat imzolangan JWT `MarketId` claim'idan olinadi (Host header fallback ataylab olib tashlangan). Claim bo'lsa, har so'rovda bitta PK lookup (`Markets.Id`) bajariladi va eshik qayta tekshiriladi — login'dagi bilan **aynan bir xil** ustuvorlik: avval blok, keyin muddat.

```csharp
if (market is { IsBlocked: true }) { /* 423 MARKET_BLOCKED */ }
if (market is not null && market.IsSubscriptionExpired(DateTime.UtcNow)) { /* 402 SUBSCRIPTION_EXPIRED */ }
```

Middleware bu yerda `GlobalExceptionHandler`'dan o'tmaydi — javobni **o'zi to'g'ridan-to'g'ri yozadi**, lekin JSON shakli login javobi bilan bir xil (`code` maydoni mijozning global error mapper'i uchun kalit).

**423 — `MARKET_BLOCKED`** (SuperAdmin bloklagan):

```json
{
  "code": "MARKET_BLOCKED",
  "message": "Do'kon administrator tomonidan bloklangan. Iltimos, administrator bilan bog'laning.",
  "reason": "<market.BlockedReason>",
  "blockedAt": "<market.BlockedAt>",
  "statusCode": 423
}
```

**402 — `SUBSCRIPTION_EXPIRED`** (obuna muddati tugagan):

```json
{
  "code": "SUBSCRIPTION_EXPIRED",
  "message": "Obuna muddati tugagan. Iltimos, administrator bilan bog'lanib obunani yangilang.",
  "expiresAt": "<market.ExpiresAt>",
  "statusCode": 402
}
```

Agar `MarketId` claim topilmasa (autentifikatsiyalangan, lekin marketga a'zolik yo'q), middleware **403 Forbidden** qaytaradi (`{ error, message }` shakli — bu obuna eshigi emas, ruxsat masalasi).

### MARKET_BLOCKED (423) va SUBSCRIPTION_EXPIRED (402) farqi

| | `MARKET_BLOCKED` | `SUBSCRIPTION_EXPIRED` |
|---|---|---|
| HTTP status | **423 Locked** | **402 Payment Required** |
| Sabab (Market qoidasi) | `IsBlocked == true` (SuperAdmin qo'lda bloklagan) | `IsActive && !IsBlocked && ExpiresAt != null && ExpiresAt <= now` |
| Qo'shimcha maydonlar | `reason`, `blockedAt` | `expiresAt` |
| Ustuvorlik | Blok muddatdan **ustun** (ikkalasi bo'lsa 423 chiqadi) | Faqat bloklanmagan holatda yuzaga keladi |
| Mijoz reaksiyasi | Blok ekraniga yo'naltiradi | Obunani yangilash ("renew") ekraniga yo'naltiradi |
| Yechim | Administratorga murojaat | Obunani yangilash (ExpiresAt uzaytirish) |

> Semantik farq: **423** — "resurs mavjud, lekin ataylab kirish taqiqlangan"ning kanonik statusi (ma'muriy blok); **402** — to'lov/obuna lapsed'iga ishora. `ExpiresAt == null` bo'lsa market "grandfather" (ochiq) hisoblanadi va hech qachon `SUBSCRIPTION_EXPIRED` bermaydi.

---

## 6. Public market-holati endpoint

Bu endpoint per-market login sahifasi (`buildix.uz/{subdomain}/login`) uchun yagona pre-auth (autentifikatsiyasiz) kirish nuqtasi. U marketning faqat "login-eshigi" holatini oshkor qiladi, shunda SPA login formasini ko'rsatishi ("active") yoki bloklangan/muddati tugagan ogohlantirishni chiqarishi kerakligini hal qiladi. Hech qanday user, sessiya yoki biznes ma'lumoti qaytarilmaydi.

### Kontrakt

| Xususiyat | Qiymat |
|---|---|
| Marshrut | `GET /api/public/market/{subdomain}` |
| Controller | `PublicMarketController` (`[Route("api/public/market")]`, `[HttpGet("{subdomain}")]`) |
| Avtorizatsiya | `[AllowAnonymous]` — token talab qilinmaydi |
| Rate limit | `[EnableRateLimiting("public-market")]` |
| Muvaffaqiyat | `200 OK` + `PublicMarketStateDto` |
| Topilmadi | `404 Not Found` + `{ "message": "Do'kon topilmadi." }` |
| Middleware | `TenantResolutionMiddleware` `/api/public/` prefiksini skip qiladi (pre-auth chaqiruvda tenant konteksti yo'q) |

Controller yupqa: chaqiruvni to'liq `IMarketService.GetPublicStateBySubdomainAsync` ga topshiradi, `null` bo'lsa `NotFound`, aks holda `Ok(state)` qaytaradi.

```csharp
var state = await _marketService.GetPublicStateBySubdomainAsync(subdomain, cancellationToken);
if (state is null)
    return NotFound(new { message = "Do'kon topilmadi." });
return Ok(state);
```

### Javob shakli — PublicMarketStateDto

```json
{
  "subdomain": "toshkent-market",
  "marketName": "Toshkent Market",
  "state": "active",
  "expiresAt": "2026-12-31T00:00:00Z"
}
```

| JSON maydon | Tip | Izoh |
|---|---|---|
| `subdomain` | `string` | Normalizatsiya qilingan slug (`Trim` + `ToLowerInvariant`) |
| `marketName` | `string` | Market nomi (`Market.Name`) — login ekranida ko'rsatiladi |
| `state` | `string` | `active` \| `expired` \| `blocked` |
| `expiresAt` | `DateTime?` | Obuna tugash sanasi (UTC); `null` = grandfather (ochiq) |

`expiresAt` login ekranida "renew notice"ni to'ldirish uchun beriladi; `active` holatida ham (agar mavjud bo'lsa) kelib chiqishi mumkin.

### Holat hisoblash (service tomonida)

`MarketService.GetPublicStateBySubdomainAsync` quyidagi ketma-ketlikda ishlaydi:

1. **Slug normalizatsiya** — `subdomain?.Trim().ToLowerInvariant()`. Bo'sh yoki `null` bo'lsa darhol `null` qaytadi (→ 404).
2. **Qidiruv** — `Markets` jadvalidan `AsNoTracking` bilan `m.Subdomain == slug` bo'yicha `FirstOrDefaultAsync`.
3. **Ko'rinmaslik filtri** — `market is null || !market.IsActive` bo'lsa `null` qaytadi (→ 404). Noma'lum slug va soft-deleted (`IsActive == false`) market bir xil ko'riladi: ikkalasi ham "bu eshik ochilmaydi". "Bunday slug yo'q" va "o'chirilgan" holatlari mijozga ajratilmaydi (ma'lumot sizib chiqmasligi uchun).
4. **State prioriteti** — `blocked` > `expired` > `active`:

```csharp
var now = DateTime.UtcNow;
var state = market.IsBlocked ? "blocked"
    : market.IsSubscriptionExpired(now) ? "expired"
    : "active";
```

| Shart | `state` |
|---|---|
| `market.IsBlocked` | `blocked` |
| `!IsBlocked && IsSubscriptionExpired(now)` | `expired` |
| Aks holda | `active` |

Muhim: `inactive` (`!IsActive`) holati bu javobda umuman chiqmaydi — u yuqorida 404 ga aylanadi. Ya'ni state faqat ko'rinadigan (active-doori mavjud) marketlar uchun `active`/`expired`/`blocked` bo'ladi. Obuna qoidasi (`IsBlocked`, `IsSubscriptionExpired`) Market entity'sining yagona manbasidan olinadi — controller yoki DTO'da takrorlanmaydi.

### Xavfsizlik va maxfiylik

- **Anonim** — endpoint pre-auth, shu sabab hech qachon token, username, sessiya yoki biznes ma'lumotini qaytarmaydi; faqat login gate uchun zarur to'rt maydon (`subdomain`, `marketName`, `state`, `expiresAt`).
- **Rate limit** — `public-market` policy anonim endpoint'ni abuse/enumeratsiyadan himoya qiladi.
- **Yagona 404** — noma'lum va o'chirilgan market bir xil `404` + `{ message }` qaytaradi, mavjudlik farqi oshkor bo'lmaydi.
- **Middleware skip** — `/api/public/` yo'llari tenant resolution'dan o'tkazib yuboriladi, chunki bu bosqichda tenant (market) konteksti hali aniqlanmagan.

### Foydalanuvchi oqimi

```
Klient: buildix.uz/{subdomain}/login  ochiladi
   │
   ▼
GET /api/public/market/{subdomain}   (anonim)
   │
   ├─ 404  → "Do'kon topilmadi." (noma'lum yoki o'chirilgan)
   │
   └─ 200 { state }
         ├─ active   → login formasi ko'rsatiladi
         ├─ expired  → obunani yangilash ogohlantirishi (expiresAt bilan)
         └─ blocked  → adminga murojaat ogohlantirishi
```

---

## 7. To'liq API kontrakt (o'zgargan / yangi endpointlar)

Quyida shu ish doirasida **o'zgargan yoki yangi qo'shilgan** endpointlar keltirilgan. Barcha JSON maydon nomlari kodga aniq mos (`camelCase`, `System.Text.Json` `[JsonPropertyName]` orqali). Marshrutlar controller `[Route]` atributlaridan olingan.

> **Muhim:** Ochiq self-registration endpoint (`POST /api/Auth/Register`) **butunlay olib tashlangan** — `AuthController` da faqat izoh qoldi, `RegisterRequest` DTO ham o'chirilgan. Onboarding endi faqat SuperAdmin nazorati ostida: tashrifchi `POST /api/RegistrationRequests` yuboradi, SuperAdmin uni `.../requests/{id}/approve` bilan tasdiqlaydi yoki `.../owners` orqali qo'lda yaratadi.

---

### 1. `POST /api/Auth/Login`

Marshrut `api/[controller]/[action]` shablonidan hosil bo'ladi.

| Method | Route | Auth | Rate limit |
|---|---|---|---|
| `POST` | `/api/Auth/Login` | `AllowAnonymous` | `auth-login` policy → 429 |

**Request JSON** (`LoginRequest`):

```json
{
  "username": "olim",          // required, 3–50 belgi
  "password": "secret123",     // required, 1–100 belgi (login kuchli-parol qoidasini TEKSHIRMAYDI)
  "subdomain": "olim-market"   // optional — URL sub-path slug'idan olinadi
}
```

`subdomain` berilsa login **shu bitta marketga** cheklanadi (cross-tenant username kolliziyasini hal qiladi) va o'sha marketning obuna eshigi tekshiriladi. Bo'sh bo'lsa login market-agnostik qoladi (SuperAdmin root'dan slug'siz kiradi).

**Response JSON 200 OK** (`AuthResponse`):

```json
{
  "userId": "…",
  "username": "olim",
  "fullName": "Olim Karimov",
  "role": "Owner",
  "language": "uz",
  "accessToken": "…",
  "refreshToken": "…",
  "expiresAt": "2026-07-19T12:30:00Z",
  "permissions": ["…"],
  "marketId": 42,             // silent auto-login uchun — SuperAdmin'da null
  "subdomain": "olim-market"  // silent auto-login uchun — SuperAdmin'da null
}
```

`marketId` + `subdomain` **shu ish doirasida qo'shilgan** — klient saqlangan sessiyani URL path slug'iga solishtirib, tirik token bo'lsa login qilmasdan kiradi (silent auto-enter).

**Status kodlar:**

| Kod | Holat | Body |
|---|---|---|
| `200` | Muvaffaqiyat | `AuthResponse` |
| `400` | Shift-inactive va shu kabi rad etishlar (`InvalidOperationException`) | `{ "message": "…" }` |
| `401` | Noto'g'ri credentials (`result is null`) | `"Invalid credentials"` |
| `402` | `SUBSCRIPTION_EXPIRED` — obuna muddati tugagan market | quyidagi xato JSON |
| `423` | `MARKET_BLOCKED` — bloklangan market | quyidagi xato JSON |
| `429` | `auth-login` rate limit oshdi | rate-limit javobi |

`402`/`423` javoblari obuna enforcement qatlamidan (login vaqtida hamda `TenantResolutionMiddleware` real-time tekshiruvidan) chiqadi. Yagona xato JSON shakli:

```json
{
  "code": "SUBSCRIPTION_EXPIRED",   // yoki "MARKET_BLOCKED"
  "message": "…",
  "expiresAt": "2026-07-01T00:00:00Z", // 402 uchun; 423 uchun "reason"/"blockedAt"
  "statusCode": 402
}
```

---

### 2. `POST /api/Auth/RefreshToken`

| Method | Route | Auth | Rate limit |
|---|---|---|---|
| `POST` | `/api/Auth/RefreshToken` | `AllowAnonymous` | `auth-refresh` policy → 429 |

**Request JSON** (`RefreshTokenRequest`):

```json
{
  "accessToken": "…",   // required
  "refreshToken": "…"   // required
}
```

**Response JSON 200 OK:** yangi `AuthResponse` (Login bilan bir xil shakl, `marketId`/`subdomain` bilan).

**Status kodlar:**

| Kod | Holat | Body |
|---|---|---|
| `200` | Yangi token juftligi | `AuthResponse` |
| `401` | Yaroqsiz token (`result is null`) | `"Invalid token"` |
| `429` | `auth-refresh` rate limit | rate-limit javobi |

`401` — **bitta umumiy javob**: sabab (noma'lum / muddati o'tgan / begona / o'g'irlangan) oshkor qilinmaydi (user enumeration'ni oldini olish). Rotatsiya poygasi (ikki tab) va "javob yo'lda yo'qoldi" holatlari `AuthService` grace oynasida xayrixoh deb tanib, o'sha zanjirdan yangi juftlik beradi — shuning uchun alohida `409` shoxi **yo'q**.

---

### 3. `GET /api/public/market/{subdomain}`

Login sahifasini (`buildix.uz/{subdomain}/login`) boshqaradigan anonim, pre-auth endpoint. Marshrut `/api/public/` ostida — `TenantResolutionMiddleware` uni o'tkazib yuboradi.

| Method | Route | Auth | Rate limit |
|---|---|---|---|
| `GET` | `/api/public/market/{subdomain}` | `AllowAnonymous` | `public-market` policy → 429 |

**Request:** body yo'q; `subdomain` — path parametri.

**Response JSON 200 OK** (`PublicMarketStateDto`):

```json
{
  "subdomain": "olim-market",
  "marketName": "Olim Market",
  "state": "active",              // "active" | "expired" | "blocked"
  "expiresAt": "2026-08-01T00:00:00Z"  // null bo'lishi mumkin (grandfather)
}
```

**Status kodlar:**

| Kod | Holat | Body |
|---|---|---|
| `200` | Market topildi | `PublicMarketStateDto` |
| `404` | Slug noma'lum yoki soft-deleted | `{ "message": "Do'kon topilmadi." }` |
| `429` | `public-market` rate limit | rate-limit javobi |

Faqat login-eshigi holati qaytadi — hech qanday user/sessiya/biznes ma'lumot **yo'q**.

---

### 4. `POST /api/RegistrationRequests`

Anonim sign-up kirish nuqtasi. Controllerda ataylab **faqat bitta POST** bor — `GET`/`PUT`/`DELETE` yo'q, shu sabab navbat maxfiy qoladi (telefon raqamlar / pending holat sizib chiqmaydi).

| Method | Route | Auth | Rate limit |
|---|---|---|---|
| `POST` | `/api/RegistrationRequests` | `AllowAnonymous` | `registration-submit` policy → 429 |

**Request JSON** (`SubmitRegistrationRequestDto`):

```json
{
  "fullName": "Olim Karimov",
  "phone": "+998901234567"
}
```

**Response JSON:**

```json
{ "message": "Adminga yubordik. Admin tez orada javob beradi." }
```

**Status kodlar:**

| Kod | Holat | Body |
|---|---|---|
| `200` | Muvaffaqiyat **VA** yashiriladigan barcha xatolar (dublikat telefon, shubhali shakl) | umumiy `message` |
| `400` | Faqat `Validation` kodli xato (format bo'yicha maslahat) | `{ "message": "…" }` |
| `429` | `registration-submit` rate limit | rate-limit javobi |

Enumeration'ni oldini olish uchun muvaffaqiyat va yashirin xatolar **bir xil 200 javob** qaytaradi; haqiqiy sabab faqat server log'iga yoziladi. Yagona ochiq 4xx — format validatsiyasi (`400`) va rate limiter (`429`).

---

### 5. `POST /api/_sa/{consoleSegment}/requests/{id}/approve`

SuperAdmin so'rovni tasdiqlaydi → yangi Owner + Market + subdomain yaratiladi.

| Method | Route | Auth | Rate limit |
|---|---|---|---|
| `POST` | `/api/_sa/{consoleSegment}/requests/{id}/approve` | `Authorize(Roles = "SuperAdmin")` + yashirin segment gate | `super-admin` policy |

`{consoleSegment}` — operator `SuperAdmin:ConsoleSegment` orqali sozlaydigan opaque segment. Noto'g'ri segment bilan so'rov `SuperAdminPathGateMiddleware` da **autentifikatsiyadan oldin 404** qaytaradi (skaner konsol borligini bilmaydi). `{id}` — `:guid` bilan cheklangan.

**Request JSON** (`ApproveRegistrationRequestDto`):

```json
{
  "username": "olim",
  "password": "Str0ng!Pass",     // required + StrongPassword qoidasi
  "marketName": "Olim Market",
  "subdomain": null,             // optional — bo'sh bo'lsa tizim generatsiya qiladi
  "language": "uz",              // optional, default "uz"
  "expiresAt": "2026-08-01T00:00:00Z"  // optional — null = muddat qo'yilmagan (grandfather)
}
```

**Response JSON 200 OK** (`ApproveRegistrationResultDto`):

```json
{
  "requestId": "…",
  "userId": "…",
  "username": "olim",
  "marketId": 42,
  "marketName": "Olim Market"
}
```

Parol javobda faqat shu yerda qaytadi (SuperAdmin uni yangi egaga SMS/telefon orqali uzatishi uchun) — boshqa hech qayerda saqlanmaydi/qaytarilmaydi.

**Status kodlar:**

| Kod | Holat | Body |
|---|---|---|
| `200` | Tasdiqlandi | `ApproveRegistrationResultDto` |
| `400` | `InvalidOperationException` (masalan, username/subdomain band) | `{ "message": "…" }` |
| `401` | Caller id (JWT `NameIdentifier`) yo'q | — |
| `404` | So'rov topilmadi (`KeyNotFoundException`) | `{ "message": "So'rov topilmadi." }` |
| `404` | Noto'g'ri console segment (auth'dan oldin) | — |

---

### 6. `/api/_sa/{consoleSegment}/owners`

SuperAdmin owner ro'yxati va qo'lda yaratish (registratsiya so'rovisiz off-channel signup uchun). Auth va segment gate 5-bo'lim bilan bir xil.

| Method | Route | Auth | Request | Response |
|---|---|---|---|---|
| `GET` | `/api/_sa/{consoleSegment}/owners` | SuperAdmin + segment gate | — | `OwnerSummaryDto[]` (`IEnumerable`) |
| `POST` | `/api/_sa/{consoleSegment}/owners` | SuperAdmin + segment gate | `CreateOwnerDto` | `ApproveRegistrationResultDto` |

**GET javob elementi** (`OwnerSummaryDto`):

```json
{
  "userId": "…",
  "fullName": "Olim Karimov",
  "username": "olim",
  "phone": "+998901234567",
  "isActive": true,
  "marketId": 42,
  "marketName": "Olim Market",
  "isMarketBlocked": false,
  "createdAt": "2026-07-01T00:00:00Z"
}
```

**POST Request JSON** (`CreateOwnerDto`) — approve payload'ini takrorlaydi, ustiga so'rov beradigan applicant maydonlarini qo'shadi:

```json
{
  "fullName": "Olim Karimov",
  "phone": "+998901234567",
  "username": "olim",
  "password": "Str0ng!Pass",     // required + StrongPassword
  "marketName": "Olim Market",
  "subdomain": null,             // optional — bo'sh bo'lsa generatsiya
  "language": "uz",              // optional, default "uz"
  "expiresAt": null              // optional — Approve.expiresAt bilan bir xil semantika
}
```

**POST javob:** `ApproveRegistrationResultDto` (yuqoridagi bilan bir xil shakl).

**Status kodlar (POST):**

| Kod | Holat | Body |
|---|---|---|
| `200` | Yaratildi | `ApproveRegistrationResultDto` |
| `400` | `InvalidOperationException` (band username/subdomain va h.k.) | `{ "message": "…" }` |
| `401` | Caller id yo'q | — |
| `404` | Noto'g'ri console segment (auth'dan oldin) | — |

---

**Qo'shimcha eslatma (sessiya hayotiy sikli):** `POST /api/Auth/Logout` (`Authorize`, `auth-logout` rate limit) `RefreshTokenRequest` qabul qiladi va joriy access token'ning `jti` + `exp` da'volarini revocation ro'yxatiga qo'shadi (aks holda token o'zining 30-min TTL'igacha ishlab turardi). Bu endpoint bevosita shu ish doirasida o'zgarmagan, ammo silent auto-login/token oqimini to'ldiradi.

---

## 8. Frontend (Flutter) kontrakti — alohida repo

> **Diqqat:** Flutter frontend **bu repoda YO'Q**. Buildix backend'i (`.NET 9`, Clean Architecture) faqat API'ni ta'minlaydi; SPA/mobil klient **alohida repozitoriyada** joylashgan. Quyidagi bo'lim — Flutter jamoasi uchun **kontrakt/spec**: implementatsiya emas, balki backend bilan kelishilgan interfeys (marshrutlar, JSON maydonlari, status kodlar, kirish oqimi). Barcha endpoint yo'llari, maydon nomlari va status kodlar backendga aniq mos.

---

### 1. Routing (path asosida, DNS subdomain EMAS)

Marshrutlash **path segmentidan** olinadi — haqiqiy DNS subdomain ishlatilmaydi. Wildcard DNS/TLS kerak emas.

| URL shabloni | Ma'nosi |
|---|---|
| `buildix.uz/{slug}/` | Market kirish eshigi (silent auto-login tekshiruvi shu yerda) |
| `buildix.uz/{slug}/login` | Market login formasi |
| `buildix.uz/` (yoki alohida root) | SuperAdmin konsoli — **slug'siz** |

- `{slug}` = market'ning `Subdomain` qiymati (URL-safe, unique). SPA uni **birinchi path segmentidan** oladi va API chaqiruvlariga uzatadi.
- nginx SPA fallback: `/{slug}/*` → `index.html` (`try_files ... /index.html`). `/api/*` → Kestrel proxy.
- Klient **hech qachon** Host header'ga tayanmaydi; slug faqat path'dan keladi.

---

### 2. Kirish oqimi (silent auto-login)

Foydalanuvchi `buildix.uz/{slug}/` ni ochganda quyidagi ketma-ketlik bajariladi:

```
1. slug ← path[0]
2. GET /api/public/market/{slug}
     ├─ 404                      → "Market topilmadi" ekrani (login yo'q)
     ├─ 200 state == "blocked"   → "Bloklangan" ekrani (login yo'q)
     ├─ 200 state == "expired"   → "Obuna tugadi, yangilang" ekrani (login yo'q)
     └─ 200 state == "active"    → 3-qadamga o't
3. Saqlangan sessiyani tekshir:
     session bor && session.subdomain == slug && session.marketId == market.id ?
       ├─ YO'Q  → /{slug}/login (login forma)
       └─ HA    → 4-qadamga o't
4. Tirik token bilan probe:  GET /api/Users/MyProfile  (Authorization: Bearer <accessToken>)
     ├─ 200                          → TO'G'RIDAN-TO'G'RI ERP'ga kiradi (login yo'q)
     ├─ 402 SUBSCRIPTION_EXPIRED     → "Obuna tugadi" ekrani (global error-mapper)
     ├─ 423 MARKET_BLOCKED          → "Bloklangan" ekrani (global error-mapper)
     └─ 401                          → 5-qadamga o't
5. Silent refresh:  POST /api/Auth/RefreshToken
     ├─ 200  → yangi tokenlarni saqla → ERP'ga kiradi (login yo'q)
     └─ boshqa → /{slug}/login (login forma)
```

**Muhim invariant:** kirish qарорi doim ikki tekshiruvga tayanadi — (a) public market `state` (obuna eshigi) va (b) token tirikligi. `state != active` bo'lsa, saqlangan token bo'lsa ham **login/kirish urinilmaydi**.

---

### 3. Endpoint kontrakti

| Metod + yo'l | Auth | So'rov (body) | Muvaffaqiyat | Ishlatilishi |
|---|---|---|---|---|
| `GET /api/public/market/{subdomain}` | Anonim | — | `200` market holati | Login ekranini boshqarish |
| `GET /api/Users/MyProfile` | Bearer | — | `200` profil / `401` token o'lik | Token tirikligi probe'i |
| `POST /api/Auth/RefreshToken` | (refresh token) | refresh payload | `200` yangi token juftligi | Silent refresh |
| `POST /api/Auth/Login` | Anonim | `{ username, password, subdomain }` | `200` `AuthResponse` | Login forma |

> `POST /api/Auth/Register` **MAVJUD EMAS** — ochiq self-register olib tashlangan. Ro'yxatdan o'tish faqat SuperAdmin orqali; user faqat `POST /api/RegistrationRequests` (`FullName` + `Phone`) murojaat so'rovini yubora oladi.

#### 3.1. `GET /api/public/market/{subdomain}` javobi

Anonim, rate-limited. **User/sessiya ma'lumoti YO'Q.**

```jsonc
// 200 — market topildi
{
  "subdomain":   "sardor-market",
  "marketName":  "Sardor Market",
  "state":       "active",          // "active" | "expired" | "blocked"
  "expiresAt":   "2026-12-31T00:00:00Z" // nullable (grandfather bo'lsa null)
}
```

| `state` | Klient harakati |
|---|---|
| `active` | Silent auto-login oqimini davom ettir (2-bo'lim, 3-qadam) |
| `expired` | "Obuna muddati tugadi, administrator bilan bog'laning" ekrani — **login yo'q** |
| `blocked` | "Market bloklangan" ekrani — **login yo'q** |
| `404` (javob tanasi yo'q) | Noma'lum yoki soft-deleted (`!IsActive`) slug → "Market topilmadi" |

> Public endpoint javobi faqat to'rt maydon qaytaradi: `subdomain`, `marketName`, `state`, `expiresAt`. Ekran matnini (`expired`/`blocked`) klient `state` bo'yicha o'zi tanlaydi — server alohida `message` maydonini bermaydi.

#### 3.2. `POST /api/Auth/Login`

```jsonc
// Request body
{
  "username":  "sardor",
  "password":  "•••••••",
  "subdomain": "sardor-market"   // URL slug'idan; login shu marketga cheklab qidiriladi
}
```

`subdomain` berilishi cross-tenant username kolliziyasini hal qiladi (server userni **shu market ichida** qidiradi) va login vaqtida obuna eshigini tekshiradi. SuperAdmin root'dan kirganda `subdomain` **yuborilmaydi** (null).

```jsonc
// 200 — AuthResponse (klient uchun muhim yangi maydonlar)
{
  "accessToken":  "eyJ...",
  "refreshToken": "...",
  "marketId":     12,               // int?  — sessiyani slug'ga bog'lash uchun
  "subdomain":    "sardor-market"   // string? — silent auto-login solishtiruvi uchun
  // ... qolgan mavjud maydonlar (user ma'lumoti, muddat va h.k.)
}
```

Muvaffaqiyatdan so'ng klient **`marketId` + `subdomain` bilan birga** sessiyani saqlaydi (4-bo'lim).

---

### 4. Sessiya saqlash modeli

Saqlangan sessiya kamida quyidagilarni o'z ichiga oladi:

```jsonc
{
  "accessToken":  "…",
  "refreshToken": "…",
  "marketId":     12,               // AuthResponse.marketId
  "subdomain":    "sardor-market"   // AuthResponse.subdomain
}
```

- Silent auto-login'da (2-bo'lim, 3-qadam) klient **`session.subdomain == slug` VA `session.marketId == market.id`** ekanini tekshiradi. Mos kelmasa (boshqa marketga tegishli sessiya) — login forma ko'rsatiladi, eski token ishlatilmaydi.
- Autentifikatsiyalangan tenant **doim imzolangan JWT `MarketId` claim'idan** aniqlanadi (backend tomonida). Saqlangan `subdomain`/`marketId` faqat **klient tomonidagi** solishtiruv uchun — server bunga ishonmaydi.
- Token saqlash: web'da `localStorage`/secure storage; mobil'da xavfsiz storage. Tokenlar URL query'da **hech qachon** uzatilmaydi.

---

### 5. Global error-mapper (real-time enforcement)

Har qanday API javobida (nafaqat login) HTTP interceptor quyidagi kodlarni ushlaydi va butun ilovani mos ekranga o'tkazadi — **faol sessiya ham** darhol to'xtaydi. Bu backend'ning middleware real-time bloki bilan mos.

| HTTP status | `code` | Klient reaksiyasi |
|---|---|---|
| `402` | `SUBSCRIPTION_EXPIRED` | Sessiyani to'xtat → "Obuna tugadi, yangilang" ekrani |
| `423` | `MARKET_BLOCKED` | Sessiyani to'xtat → "Market bloklangan" ekrani |
| `401` | — | Silent `POST /api/Auth/RefreshToken`; muvaffaqiyatsiz → login forma |

**Xato JSON shakli** (barcha domain xatolarida bir xil struktura):

```jsonc
// 402 — obuna tugagan
{
  "code":       "SUBSCRIPTION_EXPIRED",
  "message":    "Obuna muddati tugagan. Iltimos, administrator bilan bog'lanib obunani yangilang.",
  "expiresAt":  "2026-06-01T00:00:00Z",
  "statusCode": 402
}

// 423 — bloklangan
{
  "code":       "MARKET_BLOCKED",
  "message":    "…",
  "blockedAt":  "2026-05-10T00:00:00Z",   // (yoki "reason")
  "statusCode": 423
}
```

Klient **`code`** maydoni bo'yicha ajratadi (HTTP statusni ham tasdiqlash sifatida ishlatadi). Diskriminatsiya qiluvchi qo'shimcha maydon holatga qarab farq qiladi: `SUBSCRIPTION_EXPIRED` → `expiresAt`; `MARKET_BLOCKED` → `blockedAt`/`reason`.

---

### 6. SuperAdmin konsoli

- **Alohida root** (slug'siz) manzilida joylashadi — market path segmenti YO'Q.
- Login **`subdomain` yubormaydi** (`POST /api/Auth/Login` bodysida `subdomain: null`). SuperAdmin (`MarketId == null`) obuna/blok tekshiruvlaridan **ozod** — konsolga har doim kira oladi.
- Global error-mapper'dagi 402/423 mantiqи SuperAdmin sessiyasiga **tegmaydi** (u market'ga bog'lanmagan).

---

### 7. Klient tomonidagi qisqa cheklovlar (invariantlar)

- Silent auto-login **faqat** `state == active` va `session.subdomain == slug` bo'lganda urinadi.
- `state` `expired`/`blocked` bo'lsa yoki `404` bo'lsa — login formasi **ko'rsatilmaydi**.
- Har qanday endpoint'da `402`/`423` kelsa — joriy ekrandan qat'i nazar renew/blok ekraniga o'tiladi (real-time eshik yopilishi).
- Tokenlar hech qachon URL/query'ga qo'yilmaydi; faqat `Authorization: Bearer` header'da.

---

## 9. Ops / infratuzilma (bu repoda emas)

Bu bo'lim ilova tashqarisidagi (out-of-repo) sozlamalarni tavsiflaydi: DNS, reverse-proxy (nginx), TLS va domen o'zgarishi. Bu artefaktlar Buildix kod bazasida **yo'q** — ular server operatori (DevOps) tomonidan boshqariladi. Bu yerda faqat `.NET` tomonidagi sozlamalar (`Buildix.API/Program.cs`, `Buildix.API/appsettings.Production.json`) bilan bog'lanish nuqtalari va ular qanday to'ldirilishi kerakligi ko'rsatiladi.

Asosiy me'moriy qaror: routing **PATH asosida** (`buildix.uz/{subdomain}/login`), haqiqiy DNS subdomain (`{subdomain}.buildix.uz`) EMAS. Shu sabab wildcard DNS ham, wildcard CORS/TLS ham kerak emas — brauzer nuqtai nazaridan barcha marketlar bitta `https://buildix.uz` origin ostida yashaydi.

### Deploy topologiyasi

```
Internet ──HTTPS(443)──▶ nginx (TLS termination, edge)
                           │
                           ├── /api/*   ──proxy_pass──▶ Kestrel (127.0.0.1:8080, plain HTTP)
                           ├── /hubs/*  ──proxy(WS)───▶ Kestrel (SignalR: /hubs/sales)
                           └── /{slug}/* ─────────────▶ index.html (Flutter web SPA fallback)
```

- Kestrel `http://0.0.0.0:8080` da tinglaydi (`PORT` / `ASPNETCORE_HTTP_PORTS` / `ASPNETCORE_URLS` env orqali), Docker compose uni `127.0.0.1` ga bog'laydi — Kestrel internetdan bevosita ochilmaydi.
- TLS'ni faqat nginx yakunlaydi. `.NET` tomonida `UseHttpsRedirection` **ataylab o'chirilgan** (`Program.cs`), aks holda Kestrel HTTP ko'radi va 308 redirect tsikli hosil bo'ladi. HTTP→HTTPS redirect'ni nginx qiladi.
- `.NET` tomonda `UseHsts()` yoqilgan: `max-age=180 kun`, `includeSubDomains`, `preload` YO'Q (preload domen darajasidagi majburiyat — operator qaror qiladi).

### DNS

| Yozuv | Qiymat | Izoh |
|-------|--------|------|
| `buildix.uz` (A/AAAA) | server IP | Asosiy va yagona zarur yozuv |
| `www.buildix.uz` (A/CNAME) | server IP / `buildix.uz` | Ixtiyoriy; `www`→apex redirect |
| Wildcard `*.buildix.uz` | — | **KERAK EMAS** — routing path-based |

### nginx (repoda yo'q)

Ikki asosiy `location` blok kerak: SPA fallback va API proxy. `/api/*` va `/hubs/*` Kestrel'ga uzatiladi; qolgan barcha yo'llar (`/{slug}/...`) Flutter web `index.html` ga tushadi (klient-tomon routing).

```nginx
server {
    listen 443 ssl http2;
    server_name buildix.uz www.buildix.uz;

    # ... ssl_certificate / ssl_certificate_key ...

    # API + statik yuklamalar (/api/uploads/... shu yerdan o'tadi)
    location /api/ {
        proxy_pass         http://127.0.0.1:8080;
        proxy_set_header   Host              $host;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
        # DIQQAT: X-Forwarded-Host YUBORILMAYDI — .NET uni ishonmaydi (pastga qarang)
    }

    # SignalR (WebSocket upgrade)
    location /hubs/ {
        proxy_pass         http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header   Upgrade    $http_upgrade;
        proxy_set_header   Connection "upgrade";
        proxy_set_header   Host              $host;
        proxy_set_header   X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header   X-Forwarded-Proto $scheme;
    }

    # SPA fallback: /{slug}/login, /{slug}/... hammasi index.html ga
    location / {
        root      /var/www/buildix;   # Flutter web build
        try_files $uri $uri/ /index.html;
    }
}

# HTTP → HTTPS (edge redirect; .NET buni qilmaydi)
server {
    listen 80;
    server_name buildix.uz www.buildix.uz;
    return 301 https://buildix.uz$request_uri;
}
```

Muhim jihatlar:
- `/api/uploads/...` uchun alohida blok shart emas — mahsulot rasmlari `.NET` ichida shu prefiks ostida (`RequestPath = "/api/uploads"`) statik xizmat qilinadi va mavjud `/api/` proxy orqali yetib boradi.
- Public login ekrani `GET /api/public/market/{subdomain}` (anonim) chaqiradi — bu ham oddiy `/api/` proxy orqali o'tadi, yangi blok kerak emas.

#### X-Forwarded-* sarlavhalari

`Program.cs` `UseForwardedHeaders` ni faqat `X-Forwarded-For` va `X-Forwarded-Proto` uchun yoqadi; **`X-Forwarded-Host` ataylab yoqilmagan**.

| Sarlavha | .NET ishonadi? | Nima uchun |
|----------|:---:|-----------|
| `X-Forwarded-Proto` | Ha | `Request.IsHttps` / JWT `RequireHttpsMetadata` to'g'ri ishlashi uchun |
| `X-Forwarded-For` | Ha | Rate-limiter IP bo'yicha partition qilishi uchun (haqiqiy klient IP) |
| `X-Forwarded-Host` | **Yo'q** | Host-header injection'ning oldini olish — nginx `Host` ni almashtirmaydi |

Qo'shimcha nuqtalar:
- `ForwardLimit = 2` (nginx → ichki hop → Kestrel zanjiri).
- Faqat RFC1918 xususiy tarmoqlar (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`) ishonchli proxy sifatida ro'yxatga olingan; ro'yxat `Clear()` qilinmaydi, aks holda har qanday manba `X-Forwarded-For` ni soxtalashtira olardi.

### `.NET` config o'zgarishlari (strotech.uz → buildix.uz)

Ikki sozlama domen migratsiyasida yangilanishi shart. `appsettings.Production.json` hozir eski domen (`strotech.uz`) ni saqlaydi.

#### 1) `AllowedHosts` (Host-header filtri)

| Holat | Qiymat |
|-------|--------|
| Hozir | `strotech.uz;www.strotech.uz;localhost;127.0.0.1` |
| Kerak | `buildix.uz;www.buildix.uz;localhost;127.0.0.1` |

`localhost` **saqlanishi shart** — Docker healthcheck `wget http://localhost:8080/health` orqali `MapGet("/health")` endpoint'iga uradi.

#### 2) `Cors:AllowedOrigins`

`ProductionCors` policy `WithOrigins(...).AllowAnyMethod().AllowAnyHeader().AllowCredentials()` bilan qurilgan. `AllowCredentials()` yoqilgani sabab **wildcard (`*`) origin ishlatib bo'lmaydi** (CORS spetsifikatsiyasi taqiqlaydi). Ammo routing path-based bo'lgani uchun **wildcard subdomain ham kerak emas** — barcha marketlar bitta origin ostida:

```jsonc
// appsettings.Production.json
"Cors": {
    "AllowedOrigins": ["https://buildix.uz"]
}
```

Muqobil (env orqali, `Program.cs` uchala formani qabul qiladi):

```bash
Cors__AllowedOrigins=https://buildix.uz
# yoki indeksli:
Cors__AllowedOrigins__0=https://buildix.uz
```

- `configuredOrigins` bo'sh bo'lsa, prod'da barcha cross-origin so'rovlar rad etiladi va startup'da `Warning` log yoziladi.
- Ro'yxat vergul bilan bo'linadi, trimlanadi va `OrdinalIgnoreCase` bo'yicha dedup qilinadi.
- Agar operator apex bilan birga `www` ni ham to'g'ridan-to'g'ri ochsa (redirect qilmasa), unda `"https://www.buildix.uz"` ni ham qo'shish kerak.

### Rate-limit siyosatlari (edge bilan bog'liq)

Rate-limiting `.NET` ichida (`UseRateLimiter`), nginx'da emas. IP partitioning `X-Forwarded-For` orqali kelgan haqiqiy klient IP'siga tayanadi — shuning uchun nginx yuqoridagidek `X-Forwarded-For` ni to'g'ri uzatishi kritik. Anonim/ommaviy endpoint'lar uchun tegishli policy'lar:

| Policy | Endpoint | Limit (per IP) | Izoh |
|--------|----------|:---:|------|
| `public-market` | `GET /api/public/market/{subdomain}` | 30/min | Har login-sahifa yuklanishida uriladi; slug-enumeration skanerlarini cheklaydi |
| `registration-submit` | `POST /api/RegistrationRequests` | 3/min | Anonim ro'yxat so'rovi; captcha yo'q, shuning uchun juda tor |
| `auth-login` | `POST /api/Auth/Login` | 30/min | NAT-do'st, brute-force'ni cheklaydi (sliding window) |
| `auth-refresh` | `POST /api/Auth/RefreshToken` | 60/min | — |

Barchasi sliding-window (6 segment/min) — window chegarasida burst qilib limitni 2x oshirishning oldini oladi.

### Enforcement javoblari (proxy uchun ma'lumot)

Obuna eshigi `.NET` ichida (login + middleware) tekshiriladi; nginx bu status kodlarni shaffof uzatishi kerak (maxsus xatolik sahifalarga almashtirmasdan). Klient bu JSON'ni o'zi qayta ishlaydi:

| Holat | HTTP status | `code` | JSON qo'shimcha maydon |
|-------|:---:|--------|------------------------|
| Market bloklangan | `423` | `MARKET_BLOCKED` | `blockedAt` |
| Obuna muddati tugagan | `402` | `SUBSCRIPTION_EXPIRED` | `expiresAt` |

Xato JSON umumiy shakli: `{ code, message, expiresAt|reason|blockedAt, statusCode }`.

### Migratsiya cheklisti (domen o'zgarishi)

- [ ] DNS: `buildix.uz` A/AAAA yozuvi server IP'siga (wildcard shart emas).
- [ ] TLS sertifikat: `buildix.uz` (+ ixtiyoriy `www.buildix.uz`).
- [ ] nginx: `server_name` → `buildix.uz`; `/api/`, `/hubs/` proxy + SPA `try_files` fallback.
- [ ] nginx: `X-Forwarded-For` va `X-Forwarded-Proto` uzatilsin; `X-Forwarded-Host` **yuborilmasin**.
- [ ] `appsettings.Production.json`: `AllowedHosts` → `buildix.uz;www.buildix.uz;localhost;127.0.0.1`.
- [ ] `Cors:AllowedOrigins` → `["https://buildix.uz"]` (yoki `Cors__AllowedOrigins` env).
- [ ] Flutter web build `index.html` bilan SPA fallback ildizi (`root`) to'g'ri.

---

## 10. Xavfsizlik invariantlari

Ushbu bo'lim Buildix ko'p-ijarali (multi-tenant) autentifikatsiya va tenant-izolyatsiya modelining buzilmasligi shart bo'lgan qat'iy qoidalarini (invariantlarini) belgilaydi. Har bir invariant kodda amalga oshirilgan; hujjatning maqsadi shu qoidalarni yagona haqiqat manbasi sifatida qat'iylashtirish.

### INV-1: Autentifikatsiyalangan tenant faqat imzolangan JWT `MarketId` claim'idan olinadi

Foydalanuvchi kimligi tasdiqlangandan so'ng, so'rov qaysi marketga (tenantga) tegishli ekani **faqat va faqat** imzolangan JWT ichidagi `MarketId` claim'i asosida aniqlanadi. URL slug (sub-path), `Host` header yoki har qanday boshqa klient tomonidan boshqariladigan signal tenant manbasi sifatida **hech qachon** ishlatilmaydi.

`TenantResolutionMiddleware` da bu quyidagicha qat'iylashgan:

```csharp
var marketIdClaim = context.User?.FindFirst("MarketId")?.Value;

if (!string.IsNullOrEmpty(marketIdClaim) && int.TryParse(marketIdClaim, out var tokenMarketId))
{
    // ...real-time enforcement...
    context.Items["MarketId"] = tokenMarketId;   // tenant faqat shu yerda o'rnatiladi
    await _next(context);
    return;
}
```

`context.Items["MarketId"]` — quyi oqimdagi barcha kontrollerlar va so'rov filtrlari uchun yagona ishonchli tenant manbai. U faqat `int.TryParse` bilan tasdiqlangan token claim'idan to'ldiriladi.

**Muhim tarixiy invariant (regressiyaga qarshi):** ilgari mavjud bo'lgan `Host` header bo'yicha fallback **qayta tiklanmasligi kerak**. Sababi kodda hujjatlashtirilgan: `Host` orqali topilgan market uchun foydalanuvchi a'zoligi (membership) hech qachon tekshirilmagan — natijada `MarketId` claim'isiz token bilan istalgan tenant ichida ishlash mumkin bo'lar edi (Owner roli esa barcha permission tekshiruvlarini chetlab o'tadi). Claim bo'lmasa — INV-6 bo'yicha `403`.

### INV-2: Slug (sub-path) faqat pre-auth signal, hech qachon tenant kaliti emas

URL slug (`buildix.uz/{subdomain}/login`) autentifikatsiyadan **oldingi** bosqichda cheklangan, aniq belgilangan vazifalarni bajaradi:

| Slug ruxsat etilgan pre-auth vazifa | Izoh |
| --- | --- |
| Login ekranini boshqarish | `GET /api/public/market/{subdomain}` orqali market holatini ko'rsatish |
| Obuna eshigini tekshirish | Login vaqtida `active/expired/blocked` holatini aniqlash |
| Login kandidatini cheklash | Cross-tenant username kolliziyasini hal qilish uchun qidiruvni shu marketga cheklash |
| Silent auto-login | Saqlangan sessiyadagi `subdomain` ni path slug'iga solishtirish |

Slug **hech qachon** avtorizatsiya qarori uchun ishlatilmaydi: post-auth so'rovda tenant qaysi slug bilan kelganidan qat'i nazar, `MarketId` claim'i hukmron. Klient slug'ni o'zgartirsa ham, imzolangan token boshqa tenantga ruxsat bermaydi — bu INV-1 ning bevosita natijasi.

### INV-3: Public endpoint hech qanday sessiya ma'lumoti chiqarmaydi va rate-limited bo'ladi

`GET /api/public/market/{subdomain}` anonim (pre-auth) endpoint bo'lib, `TenantResolutionMiddleware` da `/api/public/` prefiksi bilan tenant rezolyutsiyasidan **chetlab o'tiladi** (skip-list):

```csharp
"/api/public/",   // Public market-state — pre-auth, no tenant
```

Invariantlar:

- **Faqat login ekranini boshqaradigan minimal maydonlar qaytariladi:** `{ subdomain, marketName, state, expiresAt }`, bunda `state ∈ { active, expired, blocked }`.
- Hech qanday sessiya, token, foydalanuvchi identifikatori, ichki ID yoki maxfiy ma'lumot chiqmaydi.
- Noma'lum yoki soft-deleted market uchun `404` qaytariladi (enumeratsiya orqali marketlar ro'yxatini yig'ib olishni cheklaydi).
- Endpoint anonim va qimmat resurslarni ochmagani uchun **rate-limited** bo'lishi shart — brute-force slug enumeratsiyasi va holat probing'iga qarshi.

### INV-4: Real-time enforcement har bir so'rovda, epoch/revocation bilan bir qatorda

Token berilgandan keyin ham obuna/block holati **keyingi so'rovdayoq** kuchga kiradi — access token muddati (masalan, 30 daqiqa) tugashini kutmasdan. `TenantResolutionMiddleware` har bir tenant-scoped so'rovda `Markets` jadvaliga bitta PK-lookup qiladi va qoidani entity ustidan tekshiradi (subscription qoidasi entityda joylashgani uchun login yo'li va public state endpoint bilan drift qilmaydi).

Enforcement natijalari:

| Holat | HTTP status | `code` | Qo'shimcha JSON maydonlar |
| --- | --- | --- | --- |
| `IsBlocked == true` | `423` | `MARKET_BLOCKED` | `reason` (`BlockedReason`), `blockedAt`, `statusCode: 423` |
| `IsSubscriptionExpired(UtcNow) == true` | `402` | `SUBSCRIPTION_EXPIRED` | `expiresAt`, `statusCode: 402` |

Har ikki xato bir xil tuzilishga ega (`code`, `message`, holat-maydoni, `statusCode`) — Flutter klientning global error-mapper'i `code` bo'yicha tegishli ekranga (block / renew) yo'naltiradi.

```csharp
if (market is { IsBlocked: true }) { /* 423 MARKET_BLOCKED */ }
if (market is not null && market.IsSubscriptionExpired(DateTime.UtcNow)) { /* 402 SUBSCRIPTION_EXPIRED */ }
```

Bu real-time tekshiruv token ichidagi epoch / revocation mexanizmini **almashtirmaydi, balki to'ldiradi**: JWT imzosi va amal muddati o'z holicha ishlaydi, tenant-eshigi enforcement esa uning ustiga qo'shimcha, ma'lumotlar bazasidan olingan qatlam sifatida joylashadi. Yagona obuna qoidasi (`active/expired/blocked/inactive`) `Market` entity'da saqlanadi va login, middleware hamda public endpoint uchun bir xil manba bo'ladi.

### INV-5: SuperAdmin obuna/block eshigidan ozod, lekin cross-tenant izolyatsiya saqlanadi

SuperAdmin cross-tenant konsol bo'lib, uning JWT'sida `MarketId` claim'i **bo'lmaydi** (u bitta marketni emas, barchasini boshqaradi). Shu sababli:

- `ClaimTypes.Role == "SuperAdmin"` bo'lganda tenant rezolyutsiyasi va obuna/block tekshiruvi **o'tkazib yuboriladi** — SuperAdmin bloklangan yoki muddati tugagan marketni ham boshqara olishi kerak.
- SuperAdmin konsol yo'llari (`/api/_sa/`) skip-list orqali ham tenant rezolyutsiyasidan chetda qoladi.

```csharp
var roleClaim = context.User?.FindFirst(ClaimTypes.Role)?.Value;
if (roleClaim == "SuperAdmin") { await _next(context); return; }
```

Bu ozodlik faqat obuna/block eshigiga tegishli; SuperAdmin harakatlari o'z avtorizatsiya tekshiruvlariga bo'ysunadi.

### INV-6: Ma'lum yo'llardan tashqari, tenantsiz avtorizatsiyalangan so'rov taqiqlanadi (fail-closed)

Tizim standart holatda **fail-closed**: avtorizatsiyalangan, SuperAdmin bo'lmagan foydalanuvchi tekshirilib bo'lgan `MarketId` claim'isiz tenant-scoped so'rov yuborsa, `403 Forbidden` qaytariladi va so'rov quyi oqimga o'tmaydi.

```csharp
context.Response.StatusCode = StatusCodes.Status403Forbidden;
await context.Response.WriteAsJsonAsync(new
{
    error = "Forbidden",
    message = "Market topilmadi. Iltimos, tizimga qaytadan kiring yoki administrator bilan bog'laning."
});
```

Tenant rezolyutsiyasidan chetlab o'tishga ruxsat etilgan yo'llar aniq belgilangan (skip-list) va cheklangan: `/api/Auth/Login`, `/api/Auth/Register`, `/api/Auth/RefreshToken`, `/api/Auth/Logout`, `/api/_sa/`, `/api/RegistrationRequests`, `/api/public/`, `/health`, `/swagger`, `/privacy`, `/hubs`. Bu ro'yxatga endpoint qo'shish tenant izolyatsiyasini zaiflashtirishi mumkinligi uchun ehtiyotkorlik bilan qilinadi.

> Eslatma: `/api/Auth/Register` endpoint'ining o'zi olib tashlangan bo'lsa-da, uning yo'li tarixiy sabablarga ko'ra skip-list'da qolgan (zararsiz — bunday marshrut endi controllerda mavjud emas).

Qo'shimcha invariant: agar foydalanuvchi umuman autentifikatsiyalanmagan bo'lsa, middleware tenant xatosini o'z ustiga olmaydi — so'rov oddiy auth pipeline'ga o'tadi (`[Authorize]` uchun `401`, `[AllowAnonymous]` uchun davom). Bu haqiqiy auth muvaffaqiyatsizligini "Market topilmadi" xabari bilan yashirib qo'yishning oldini oladi.

### INV-7: Registratsiya faqat SuperAdmin nazorati ostida

Ochiq `POST /api/Auth/Register` endpoint'i **olib tashlangan** — yangi market/foydalanuvchi yaratish faqat SuperAdmin orqali amalga oshiriladi. Foydalanuvchi `POST /api/RegistrationRequests` (anonim, faqat `FullName` + `Phone`) orqali murojaat qoldiradi; SuperAdmin market nomi, username, password (ixtiyoriy `ExpiresAt`) kiritib tasdiqlaydi yoki qo'lda yaratadi, tizim esa subdomain slug'ini generatsiya qiladi. Bu invariant tenant maydonining nazoratsiz kengayishini va o'zini-o'zi tayinlagan Owner hisoblarini oldini oladi.

---

## 11. Test va verifikatsiya

Bu bo'lim sub-path login + obuna qayta ishlashini qamrab oluvchi avtomatlashtirilgan testlarni, ularning natijasini va qo'lda o'tkaziladigan end-to-end (e2e) qadamlarni bayon qiladi.

### Test strategiyasi

Testlar `Buildix.Tests` loyihasida, **xUnit** freymvorkida yozilgan. Yangi qamrov `AuthSubscriptionTests.cs` faylida jamlangan va quyidagi yondashuvga tayanadi:

- **Haqiqiy `AppDbContext` (EF Core InMemory)** ustida ishlaydi — servislar (`AuthService`, `MarketService`) mock qilinmaydi, faqat DB provayderi in-memory. Bu obuna qoidasi, tenant-scoping va DB so'rovlarini haqiqiy ijro yo'lida sinaydi.
- **Haqiqiy `JwtService`** ishlatiladi (test `Jwt:Key` bilan), shuning uchun happy-path token haqiqatan ham imzolanadi va `AccessToken` bo'sh emasligi tekshiriladi.
- Yon-bog'liqliklar (`IRevokedTokenStore`, no-slug login uchun `IJwtService`) `NSubstitute` orqali stub qilinadi; login urinishlari `InMemoryLoginAttemptTracker` bilan hisoblanadi.
- Har bir test `TestHarness` orqali `UnitOfWork`, `Db`, `Market`, `Audit` bog'liqliklarini yig'adi; ma'lumot qo'yilgach `ChangeTracker.Clear()` chaqirilib, o'qish yo'li toza (keshsiz) holatda sinaladi.

### Amalga oshirilgan testlar (12 yangi)

`AuthSubscriptionTests.cs` dagi 12 ta test to'rt guruhga bo'linadi:

#### 1. Domain qoida matritsasi (yagona manba)

`Subscription_rule_matrix` — `Market.IsSubscriptionActive(nowUtc)` va `Market.IsSubscriptionExpired(nowUtc)` metodlari 2-bo'limdagi qoidaga aynan mos ekanini tekshiradi:

| `IsActive` | `IsBlocked` | `ExpiresAt` | `IsSubscriptionActive` | `IsSubscriptionExpired` |
|---|---|---|---|---|
| `true` | `false` | kelajak | `true` | `false` |
| `true` | `false` | `null` (grandfather) | `true` | `false` |
| `true` | `false` | o'tmish | `false` | `true` |
| `true` | `true` | o'tmish | `false` | `false` (blok o'z eshigi bilan ustun) |
| `true` | `true` | kelajak | `false` | — |
| `false` | `false` | kelajak | `false` | `false` |
| `false` | `false` | o'tmish | — | `false` |

Asosiy invariant: `blocked` va `inactive` marketlar hech qachon "active" ham, "expired" ham emas — ularning o'z holatlari (`423` / soft-delete) ustun keladi.

#### 2. Ochiq market-holati endpointi

`MarketService.GetPublicStateBySubdomainAsync(slug)` (endpoint: `GET /api/public/market/{subdomain}`) uchun:

| Test | Kirish holati | Kutilgan `state` / natija |
|---|---|---|
| `Public_state_reports_active_expired_blocked_and_404` | `ExpiresAt` kelajakda | `"active"` |
| | `ExpiresAt` o'tmishda | `"expired"` |
| | `IsBlocked == true` | `"blocked"` |
| | `IsActive == false` (soft-deleted) | `null` → kontroller **404** |
| | noma'lum slug | `null` → kontroller **404** |
| `Public_state_is_case_insensitive_on_slug` | `"  ALPHA "` (bo'shliq + katta harf) | `Subdomain == "alpha"`, `state == "active"` |

Endpoint faqat `{ subdomain, marketName, state, expiresAt }` qaytaradi — hech qanday user/sessiya ma'lumoti oshkor bo'lmaydi.

#### 3. Slug bilan cheklangan (scoped) login

`AuthService.LoginAsync(LoginRequest)` uchun (`LoginRequest` ixtiyoriy `Subdomain` maydonini oladi; endpoint: `POST /api/Auth/Login`):

| Test | Ssenariy | Kutilgan natija |
|---|---|---|
| `Login_with_slug_succeeds_and_returns_market_identity` | to'g'ri slug + kredensiallar | `AuthResponse` bilan `MarketId`, `Subdomain == "alpha"`, bo'sh bo'lmagan `AccessToken` |
| `Login_slug_disambiguates_same_username_across_tenants` | ikki marketda bir xil `username`+`password`, faqat slug ajratadi | `alpha` orqali → `a.Id`; `beta` orqali → `b.Id` |
| `Login_via_wrong_market_slug_fails` | user faqat `alpha`da, login `beta` eshigidan | `null` (401 — market ichida topilmadi) |
| `Login_with_expired_slug_throws_subscription_expired` | slug marketi muddati tugagan | `SubscriptionExpiredException` (`StatusCode == 402`, `MarketId` to'g'ri) |
| `Login_with_blocked_slug_throws_market_blocked` | slug marketi bloklangan | `MarketBlockedException` (**423**) |
| `Login_with_unknown_slug_fails` | mavjud bo'lmagan slug (`"ghost"`) | `null` |
| `Wrong_password_still_fails_on_an_active_slug` | faol slug, noto'g'ri parol | `null` (obuna eshigi ochilishi parolni chetlab o'tmaydi) |

`Login_with_expired_slug_...` testida `ex.MarketId` va `ex.StatusCode == 402` alohida assert qilinadi — bu `GlobalExceptionHandlerMiddleware` chiqaradigan JSON'ning (`code`, `message`, `expiresAt`, `statusCode`) manbasini kafolatlaydi.

#### 4. Slug'siz yo'l (SuperAdmin / market-agnostik)

| Test | Ssenariy | Kutilgan natija |
|---|---|---|
| `Login_without_slug_still_enforces_expiry_on_matched_market` | slug berilmagan, lekin topilgan userning marketi muddati tugagan | `SubscriptionExpiredException` (**402**) |
| `SuperAdmin_without_market_logs_in_without_subscription_gate` | `Role.SuperAdmin`, `MarketId == null`, slug'siz | `AuthResponse` bilan `MarketId == null`, `Subdomain == null`, `Role == "SuperAdmin"` — obuna eshigidan ozod |

Bu ikki test muhim invariantni himoya qiladi: slug bermay kirgan tenant useriga ham expiry qo'llanadi, biroq SuperAdmin (`MarketId == null`) doim ozod.

### Natija

```
dotnet test Buildix.Tests
```

- **34 test — hammasi yashil** (12 yangi + 22 mavjud regressiya testi).
- **Build toza** — ogohlantirish/xatosiz kompilyatsiya.
- Yangi 12 test domen qoidasi, public holat (active/expired/blocked/404), slug login (success / disambiguation / wrong-market / expired-402 / blocked-423 / unknown), slug'siz expiry, SuperAdmin bypass va wrong-password stsenariylarini qamrab oladi.

### Qo'lda o'tkaziladigan e2e (API + PostgreSQL)

Quyidagi qadamlar HTTP darajasida, haqiqiy PostgreSQL bilan tekshirish uchun mo'ljallangan. `Jwt:Key` va `DefaultConnection` (Postgres) sozlangan bo'lishi kerak:

1. **API'ni ishga tushirish:** `dotnet run --project Buildix.API` (dev port `5050`; dev'da `/swagger` ochiq).
2. **Active oqim:** SuperAdmin bilan market yaratib `ExpiresAt` ni **kelajakka** qo'y →
   - `GET /api/public/market/{slug}` → `state: "active"`;
   - `POST /api/Auth/Login` `{ username, password, subdomain }` → **200** (`AuthResponse`da `marketId`+`subdomain`).
3. **Expired oqim:** `ExpiresAt` ni **o'tmishga** qo'y →
   - `GET /api/public/market/{slug}` → `state: "expired"`;
   - `POST /api/Auth/Login` → **402 `SUBSCRIPTION_EXPIRED`** (`{ code, message, expiresAt, statusCode }`);
   - faol token bilan istalgan himoyalangan API chaqiruvi → `TenantResolutionMiddleware` real-time **402** qaytaradi.
4. **Blocked oqim (regressiya):** `block`/`unblock` → **423 `MARKET_BLOCKED`** hamon ishlashini tasdiqla.
5. **Register olib tashlangani:** `POST /api/Auth/Register` → endi mavjud emas (**404/410**).
6. **Silent auto-login:** tirik token bilan `GET /api/Users/MyProfile` → **200** (login shart emas); access muddati o'tgach → **401** → `POST /api/Auth/RefreshToken` (silent).

### Ochiq holat: HTTP e2e hali ishga tushirilmagan

> Yuqoridagi qo'lda HTTP e2e qadamlari (3–4 va real-time middleware enforcement) **shu bosqichda ishga tushirilmagan**, chunki bu muhitda tayyor PostgreSQL bazasi yo'q. Obuna eshigi va enforcement mantig'i domain + servis darajasida InMemory testlar bilan to'liq qamrab olingan (34 test yashil), lekin uchdan-uchgacha HTTP + Postgres tekshiruvi PostgreSQL ulanishi mavjud muhitda alohida bajarilishi kerak. `verify` (yoki `run`) skill bilan API'ni haqiqiy ishga tushirib, `expired → 402` oqimini kuzatish tavsiya etiladi.
