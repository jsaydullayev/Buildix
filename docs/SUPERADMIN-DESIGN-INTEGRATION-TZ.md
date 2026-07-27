# SuperAdmin konsoli — yangi dizaynni integratsiya qilish (TZ)

**Maqsad:** `docs/Web design superadmin` (7 ta interaktiv `.dc.html` maketi) va `docs/Web design superadminPNG` (6 skrinshot) — platforma egasi (SuperAdmin) uchun **yangi dizayn** to'plamini `Buildix.Web` ga integratsiya qilish; dizayn talab qilgan, lekin backendda mavjud bo'lmagan imkoniyatlarni (tarif, obuna to'lovlari, platforma sozlamalari, bildirishnomalar) yopish.

**Manba:** 7 maketning HTML + `DCLogic` state/logikasi ekran-ma-ekran o'qildi (faqat skrinshot emas — drawer va modal'lar PNG'da yo'q) + joriy holat solishtirildi: [SuperAdminController.cs](../Buildix.API/Controllers/SuperAdminController.cs), [RegistrationRequestService.Owners.cs](../Buildix.Application/Services/RegistrationRequestService.Owners.cs), [Market.cs](../Buildix.Domain/Entities/Market.cs), [router.tsx](../Buildix.Web/src/app/router.tsx), [SuperAdminPathGateMiddleware.cs](../Buildix.API/Middleware/SuperAdminPathGateMiddleware.cs), [TZ-sub-path-login-va-obuna.md](TZ-sub-path-login-va-obuna.md). Sana: **2026-07-26**.

## Umumiy xulosa

1. **Backend yarmi allaqachon bor.** `/api/_sa/{segment}` konsoli ishlaydi: zayavkalar (list/approve/reject), owner CRUD, market block/unblock, availability check, market statistikasi. Ya'ni «Заявки» va «Магазины» ekranlarining asosi tayyor.
2. **Frontendda esa hech narsa yo'q.** `Buildix.Web/src/features/` ichida `superadmin` papkasi yo'q; router butunlay `/:subdomain/...` ga qurilgan, SuperAdmin uchun marshrut umuman mavjud emas — ya'ni bugun SuperAdmin SPA'ga **kira olmaydi**. 6 ta ekran noldan quriladi.
3. **Dizayn ataylab qoldirilgan bosqichni ochadi.** [TZ-sub-path-login-va-obuna.md:65](TZ-sub-path-login-va-obuna.md) da yozilgan: *"onlayn to'lov (Payme/Click), obuna rejalari, avtomatik eslatmalar — ko'lamdan tashqarida (hozir obunani admin qo'lda boshqaradi)"*. Yangi dizayn aynan shu uchtasini talab qiladi. Demak bu TZ o'sha rejalashtirilgan keyingi bosqich — ziddiyat emas, davomi.

**Belgilar:** 🔴 yangi (ekran yoki backend) · 🟠 muhim moslash · 🟡 o'rta · ⚪ kichik · ✅ mavjud.

---

## 1. Arxitektura qarorlari

Bu bo'lim kod yozishdan oldin qotirilishi kerak — qolgan hamma narsa shunga tayanadi.

### A1. Konsol URL sxemasi 🔴

Backend konsoli **yashirin segment** bilan himoyalangan: `/api/_sa/{consoleSegment}/...`, segment noto'g'ri bo'lsa autentifikatsiyagacha **404** ([SuperAdminPathGateMiddleware.cs](../Buildix.API/Middleware/SuperAdminPathGateMiddleware.cs)). SPA ham shu xususiyatni buzmasligi kerak.

**Qaror:** SPA marshruti `/_sa/:segment/*` — segment **URL parametri**, bundle ichida emas. Operator to'liq havolani o'zi kiritadi/saqlaydi. `superAdminApi` klienti har so'rovda segmentni route param'dan oladi:

```
/_sa/<segment>/login        → Login (slug'siz)
/_sa/<segment>/dashboard    → Панель
/_sa/<segment>/requests     → Заявки
/_sa/<segment>/stores       → Магазины
/_sa/<segment>/billing      → Подписки и оплаты
/_sa/<segment>/users        → Пользователи
/_sa/<segment>/settings     → Настройки
```

**Muhim:** segmentni `.env` / build-time konstantaga qo'ymaslik — bundle ochiq, sir chiqib ketardi. Noto'g'ri segment bilan API 404 qaytaradi → SPA "sahifa topilmadi" ko'rsatadi (konsol borligini oshkor qilmaydi).

### A2. SuperAdmin login 🔴

Bugun login faqat `/:subdomain/login` da ([router.tsx:59](../Buildix.Web/src/app/router.tsx#L59)). Backend esa slug'siz loginni allaqachon qo'llab-quvvatlaydi (`subdomain` bo'sh → market-agnostik, SuperAdmin obuna eshigidan ozod — [AuthService.Login.cs](../Buildix.Application/Services/AuthService.Login.cs)).

**Qaror:** `/_sa/:segment/login` — mavjud `LoginPage` komponenti qayta ishlatiladi, `subdomain` yuborilmaydi. Muvaffaqiyatda `role !== 'SuperAdmin'` bo'lsa **darhol logout + xato** ("Bu sahifa faqat platforma administratori uchun"), aks holda `/_sa/:segment/dashboard`. Dizayndagi `Login.dc.html` — aynan umumiy login sahifasi, ya'ni alohida dizayn kerak emas (§2.1 dagi kichik moslashlardan tashqari).

### A3. Layout va tema 🟠

Dizayn palitrasi admin panelidan **boshqacha**: akcent binafsha `#7c3aed` (adminda ko'k `#2563eb`), yon panel `#1e1145` (adminda `#0f2557`), brend yonida `SUPER` badge.

**Qaror:** yangi `SuperAdminLayout` + Tailwind'da **scoped tema**: `data-theme="super"` atributi ostida `--color-primary`, `--color-sidebar` CSS o'zgaruvchilari qayta belgilanadi. Shunda mavjud `Button`, `Badge`, `Card` komponentlari o'zgarishsiz ishlatiladi va ikkinchi dizayn-tizim paydo bo'lmaydi. Yangi token: `bg-sidebar-super`, `text-super`, `bg-super`.

Sidebar 6 band + pastda `SA Superadmin / Buildix HQ` chipi va chiqish tugmasi. «Заявки» yonida badge — yangi zayavkalar soni (polling 60s yoki mavjud SignalR hub'iga qo'shimcha event).

### A4. Til 🟡

Maketlar **faqat ruscha**. Admin/seller paneli 3 tilli (`uz`/`ru`/`en`). SuperAdmin — bitta odam (platforma egasi), shuning uchun:

**Qaror:** konsol matnlari ham `i18n` orqali (`sa.*` namespace), lekin **birinchi bosqichda faqat `ru` to'ldiriladi**, `uz`/`en` keyin. Kalitlarni hozirdan to'g'ri qo'yish — keyin qayta yozib chiqmaslik uchun.

### A5. Ruxsat modeli ⚪

SuperAdmin `PermissionKeys` tizimidan **tashqarida** — u rol bo'yicha tekshiriladi (`[Authorize(Roles = "SuperAdmin")]`). Frontendda ham `RequirePermission` emas, `RequireRole([ROLES.SuperAdmin])` ishlatiladi. Dizaynning o'zi buni tasdiqlaydi: *"Роли и доступы внутри магазина настраивает его владелец или админ — суперадмин управляет только доступом к платформе"* (Пользователи ekrani izohi).

### A6. Tenant izolyatsiyasi 🟠

Konsol so'rovlari `MarketId` claim'siz keladi → global query filter **o'chadi** (`TenantMarketId == null`, [AppDbContext.cs:33](../Buildix.Infrastructure/Data/AppDbContext.cs#L33)). Bu SuperAdmin uchun to'g'ri, lekin xavfli: har bir yangi konsol so'rovi **o'zi** market bo'yicha aniq filtrlashi shart. Yangi servislarda qoida: `_sa` yo'lidagi har bir query yo `marketId` parametri bilan, yo ataylab platforma-keng (izoh bilan).

---

## 2. Ekran-ma-ekran spetsifikatsiya

Har ekran uchun: **dizayn talabi → joriy holat → backend → ish**.

### 2.1 Login ⚪

**Dizayn:** UZ/RU/EN pill, Логин/Пароль, «Забыли пароль?», pastda **«СВЯЗЬ С АДМИНИСТРАТОРОМ»** — telefon, email, Telegram.
**Joriy:** [LoginPage.tsx:123-131](../Buildix.Web/src/features/auth/LoginPage.tsx#L123) — kontaktlar **hardcode** (`+998901234567`, `admin@buildix.uz`, `@buildix_admin`).
**Ish:** kontaktlarni platforma sozlamalaridan olish (BE-S3) — «Настройки → Контакты поддержки» ni tahrirlash **login sahifasida darhol aks etishi kerak**, aks holda sozlama yolg'on bo'ladi.
**Backend:** yangi ochiq endpoint `GET /api/public/platform-contacts` (anonim, keshlanadi, faqat telefon/telegram/email qaytaradi).
**Eslatma:** «Забыли пароль?» oqimi hozir umuman yo'q — dizaynda havola bor. Bu TZ ko'lamidan tashqari; havolani hozircha kontakt blokiga yo'naltirish tavsiya etiladi.

### 2.2 Панель — Dashboard 🔴

**Dizayn:** 4 KPI (**Активных магазинов** +N за месяц · **Новые заявки** · **Доход по подпискам** сум/мес +% · **Просрочили оплату** «доступ ограничен через N дней») · chapda **«Заявки на подключение»** (Принять/Отклонить joyida, holat darhol o'zgaradi) · o'ngda **«Требует внимания»** (qizil: muddati o'tgan do'kon; sariq: shu haftada tugaydigan obunalar) · pastda **«Магазины»** + Блокировать/Включить · header'da «Все системы работают» indikatori.
**Joriy:** ❌ yo'q.
**Backend:** yangi agregat `GET /_sa/dashboard` (BE-S8). Alohida chaqiruvlar bilan yig'ish mumkin, lekin 4 KPI + 3 ro'yxat = 7 so'rov bo'lardi — bitta endpoint arzonroq va izchil.
**Ish:** «Доход по подпискам» va «Просрочили оплату» tarif modeliga tayanadi (BE-S1) — S3 bosqichigacha bu ikki KPI **skeleton** (`—`) ko'rsatadi. Qolgan ikkitasi S1'da ishlaydi.
**«Все системы работают»:** DB + Telegram bot holati. Mavjud health-check'ga tayanadi; bo'lmasa — statik, keyin BE-S8 ga qo'shiladi.

### 2.3 Заявки 🟠

**Dizayn:** tablar **Новые / Принятые / Отклонённые / Все** (har birida son) · qidiruv (ism yoki telefon) · jadval ЗАЯВИТЕЛЬ (+izoh: «стройматериалы, Ташкент») / ТЕЛЕФОН / ПОСТУПИЛА / СТАТУС · amallar: **Принять** → **Создать магазин** → status «Подключена»; **Отклонить** → «Вернуть» (qaytarish) · qo'ng'iroq tugmasi (📞 `tel:`) · pastda ish tartibi izohi: *pozvonite → Принять → Создать магазин — do'kon va egasi akkaunti yaratiladi, login/parol SMS bilan yuboriladi*.
**Joriy:** `GET /_sa/requests`, `POST /_sa/requests/{id}/approve`, `.../reject` ✅. Lekin:
- Status enum'ida **3 qiymat** ([RegistrationRequestStatus.cs](../Buildix.Domain/Enums/RegistrationRequestStatus.cs)): `Pending / Approved / Rejected`. Dizaynda **4 bosqich**: Новая → **Принята** (qo'ng'iroq qilindi, hali do'kon yo'q) → **Подключена** (do'kon yaratildi) yoki Отклонена.
- `Approve` **darhol** owner + market yaratadi (username/parol/market nomi so'raydi) — ya'ni bugungi «Approve» dizayndagi **«Создать магазин»** ga teng, dizayndagi «Принять» esa umuman yo'q.
- Zayavkada **izoh maydoni yo'q** (faqat FullName + Phone).
**Ish:** BE-S6 (status `Accepted = 3` + `Note`), keyin ekran. `Approve` endpoint'i **o'zgarmaydi** (u «Создать магазин»), yangi yengil `POST /_sa/requests/{id}/accept` va `.../reopen` qo'shiladi.
**⚠️ Muhim:** dizaynda «Создать магазин» bir bosishda ishlaydi, lekin backend **majburiy** username/parol/market nomi/subdomain/expiresAt so'raydi. Ya'ni bu tugma **modal** ochishi shart (mavjud `CheckAvailability` endpoint'i bilan real-time band/bo'sh tekshiruvi). Buni dizayn ko'rsatmagan — modal maketini admin dizaynidagi uslubda o'zimiz quramiz.

### 2.4 Магазины 🟠

**Dizayn:** header'da hisob («6 магазинов · активных: 4 · просрочка: 1 · заблокировано: 1») · chiplar **Все/Активные/Просрочка/Заблокированные** · qidiruv (nom, egasi, shahar) · jadval МАГАЗИН (+shahar, «с марта 2025») / ВЛАДЕЛЕЦ (+telefon) / ТАРИФ / ОПЛАЧЕН ДО / ПОЛЬЗ. / СТАТУС / Блокировать · **qator bosilsa — detal drawer**: status, egasi, telefon, shahar, ulanish sanasi, **ПОДПИСКА** (tarif, narx, «Оплачен до»), 3 metrika (**Пользователей / Чеков за июль / Последняя активность**), **История оплат** (sana · usul · summa), pastda ogohlantirish *«Блокировка мгновенно закрывает вход всем пользователям магазина»* + katta blok tugmasi.
**Joriy:** `GET /_sa/owners` (`OwnerSummaryDto`: id, ism, login, telefon, isActive, marketId, marketName, isBlocked, createdAt) va `GET /_sa/owners/{id}` (+ market + statistika: mahsulot/sotuv/mijoz/xodim soni, qarz) ✅; `POST /_sa/markets/{id}/block|unblock` ✅.
**Yetishmayotgani:** tarif (BE-S1), «оплачен до» ro'yxatda (`ExpiresAt` bor, DTO'ga chiqmagan), foydalanuvchilar soni ro'yxatda, **shahar** (Market'da maydon yo'q), «Чеков за июль», «Последняя активность», **to'lovlar tarixi** (BE-S2).
**Ish:** `OwnerSummaryDto` ni kengaytirish (tariff, expiresAt, usersCount, city, status) + `OwnerDetailStatsDto` ga (checksThisMonth, lastActivityUtc, payments[]) qo'shish. Ro'yxat N+1 bo'lmasligi uchun bitta `GROUP BY` proyeksiya.
**⚠️ Semantika:** dizaynda **«Активен» + sariq «оплачен до»** = muddat yaqin, «Просрочка» = o'tgan, «Заблокирован» = qo'lda blok. Bu backenddagi uchta mustaqil holatga to'g'ri keladi: `IsSubscriptionActive` / `IsSubscriptionExpired` / `IsBlocked` ([Market.cs](../Buildix.Domain/Entities/Market.cs)) — yangi holat ixtiro qilish shart emas, faqat "yaqin" chegarasi (BE-S3 dagi `SoonThresholdDays`, default 7) qo'shiladi.

### 2.5 Подписки и оплаты 🔴

**Dizayn:** 3 tarif kartochkasi (**СТАРТ** 600 000 · **СТАНДАРТ** 1 200 000 · **ПРО** 2 400 000 сум/мес, har birida "N магазинов" va imkoniyatlar tavsifi) · tablar **Все/Скоро срок/Просрочены** + qidiruv · jadval МАГАЗИН (+«послед. оплата: 28 июня · Click») / ТАРИФ / СУММА/МЕС / ОПЛАЧЕН ДО / СТАТУС / **«Оплата получена»** tugmasi · header'da **«Напомнить всем должникам»** (bosilgach «✓ Напоминания отправлены») · pastda **«Последние платежи»** (do'kon · sana · usul · tarif · +summa).
**Joriy:** ❌ butunlay yo'q — na tarif, na to'lov jurnali.
**Backend:** BE-S1 (tarif) + BE-S2 (to'lov jurnali) + BE-S10 (eslatma).
**Ish — «Оплата получена» semantikasi (eng muhim qaror):** tugma bosilganda
1. `SubscriptionPayment` qatori yoziladi (market, tarif, summa, usul, sana, kim qabul qildi);
2. `Market.ExpiresAt = max(now, ExpiresAt) + 1 oy` — ya'ni kechikkan to'lov **muddatni yo'qotmaydi**, lekin muddati chiqqan market ham "o'tmishga" uzaytirilmaydi;
3. amal **idempotent** (`[Idempotent]` atributi mavjud — kassa endpointlarida ishlatilgan), aks holda ikki marta bosish ikki oy beradi;
4. auditga yoziladi.
Usul (Click/Payme/наличные) — hozircha **qo'lda tanlanadi** (modal), real integratsiya emas (§7 Q3).

### 2.6 Пользователи 🔴

**Dizayn:** «9 пользователей во всех магазинах · владельцев: 4 · админов: 2 · продавцов: 3» · rol chiplari **Все/Владелец/Админ/Продавец** · do'kon bo'yicha select · qidiruv (ism/login/telefon) · jadval ПОЛЬЗОВАТЕЛЬ (+login, telefon) / РОЛЬ / МАГАЗИН / ПОСЛЕДНИЙ ВХОД / СТАТУС / **Сменить пароль** / Блокировать · parol modali: yangi parol maydoni + **«Сгенерировать»** (10 belgi, chalkash harflarsiz alifbo) + ogohlantirish *«Старый пароль перестанет работать сразу. Передайте новый пароль пользователю лично.»* (min 8 belgi).
**Joriy:** ❌ platforma-keng foydalanuvchilar ro'yxati yo'q — [UsersController](../Buildix.API/Controllers/UsersController.cs) butunlay market-scoped. SuperAdmin tomonidan parol tiklash ham yo'q.
**Backend:** BE-S5.
**⚠️ Xavfsizlik:** parolni almashtirish **barcha sessiyalarni o'ldirishi** shart — bu mexanizm allaqachon bor (`InvalidateSessionsAsync` + `TokensInvalidBeforeUtc` + epoch kesh, [UserService.cs](../Buildix.Application/Services/UserService.cs)); yangi endpoint aynan shuni chaqiradi, o'zi ixtiro qilmaydi. Owner'ni bloklashda ham shu.
**⚠️ Chegara:** SuperAdmin **rollarni va ruxsatlarni tahrirlamaydi** (dizayn izohi shuni aytadi) — faqat parol, blok/aktiv. `UpdateUserAsync` dagi "Owner tahrirlanmaydi" himoyasi ([UserService.cs:195](../Buildix.Application/Services/UserService.cs#L195)) buzilmasligi kerak: yangi endpoint alohida, tor yo'l.

### 2.7 Настройки платформы 🔴

**Dizayn:** 4 blok —
- **Тарифы:** 3 narx maydoni, izoh *«цена в сумах за месяц — применяется со следующего платежа»*;
- **Правила блокировки:** «Дней отсрочки после конца подписки» (5) + 3 toggle: **Предупреждение в интерфейсе** (*жёлтая плашка владельцу и админу с 1-го дня просрочки*), **Режим «только просмотр»** (*после отсрочки: продажи заблокированы, данные видны*), **Полная блокировка входа** (*через 30 дней просрочки*);
- **SMS-уведомления магазинам:** Скоро конец подписки (3 kun oldin) · Магазин заблокирован · Логин и пароль при подключении;
- **Контакты поддержки:** telefon + Telegram (*показываются на странице входа*).
**Joriy:** ❌ platforma darajasidagi sozlamalar umuman yo'q (`MarketSettings` — har market uchun).
**Backend:** BE-S3 (sozlamalar) + BE-S4 (blok siyosati majburlash) + BE-S7 (SMS).
**⚠️ Eng qimmat band — «Режим только просмотр».** Hozir obuna tugasa middleware **butun so'rovni** 402 bilan rad etadi ([TZ-sub-path-login-va-obuna.md §5](TZ-sub-path-login-va-obuna.md)). Dizayn 3 bosqichli siyosat talab qiladi. Bu obuna semantikasining **yagona manbasini** o'zgartiradi — ehtiyotkorlik va testlar shart (BE-S4).
**⚠️ O'lik state:** maket skriptida `payOpts` (Click / Payme / Наличные toggle'lari) tayyorlangan, lekin razmetkada **ishlatilmagan** — ya'ni "to'lov usullari" kartochkasi o'ylangan-u chizilmagan. §7 Q3 da qaror kerak.

---

## 3. Backend ishlari

| Kod | Ish | Hajm | Bosqich |
|---|---|---|---|
| **BE-S1** | Tarif (`PlanCode`) modeli + limitlar | O'rta | S3 |
| **BE-S2** | `SubscriptionPayment` jurnali + «Оплата получена» | O'rta | S3 |
| **BE-S3** | `PlatformSettings` (singleton) + ochiq kontakt endpoint | Kichik | S5 |
| **BE-S4** | Grace period + «только просмотр» rejimi | **Katta/xavfli** | S5 |
| **BE-S5** | Platforma-keng foydalanuvchilar + parol tiklash/blok | O'rta | S4 |
| **BE-S6** | Zayavka: `Accepted` statusi + `Note` | Kichik | S1 |
| **BE-S7** | ~~SMS~~ → **Telegram bildirishnomalari** (SMS bekor qilindi) | O'rta | S6 ✅ |
| **BE-S8** | `GET /_sa/dashboard` agregati | Kichik | S1/S3 |
| **BE-S9** | Store ro'yxat/detal DTO kengaytmasi (+`Market.City`) | Kichik | S2 |
| **BE-S10** | «Напомнить всем должникам» (Telegram bulk eslatma) | Kichik | S6 ✅ |

### BE-S1 — Tarif modeli

```csharp
public enum PlanCode { Start = 0, Standard = 1, Pro = 2 }   // DB kontrakti — qiymatlar o'zgarmaydi
// Market.cs ga:
public PlanCode Plan { get; set; } = PlanCode.Start;
public string? City { get; set; }        // BE-S9 bilan birga
```
Narxlar **entity'da emas**, `PlatformSettings` da (BE-S3) — dizayn ularni tahrirlanadigan qilgan, va narx o'zgarsa tarixiy to'lovlar qayta yozilmasligi kerak (`SubscriptionPayment` o'z summasini saqlaydi).

**Limitlar** (Старт: 1 nuqta, ≤3 foydalanuvchi · Стандарт: ≤8 · Про: cheksiz) — `PlatformSettings` da sonlar, majburlash `UserService.CreateUserAsync` da: limitdan oshsa 400 «Tarif bo'yicha foydalanuvchilar limiti tugagan». **Qaror kerak** (§7 Q1): qattiq blok yoki faqat konsolda ogohlantirish.

### BE-S2 — Obuna to'lovlari

```csharp
public class SubscriptionPayment : BaseEntity
{
    public int MarketId { get; set; }
    public PlanCode Plan { get; set; }          // to'lov paytidagi tarif
    public decimal AmountUzs { get; set; }      // to'lov paytidagi narx (tarix qotadi)
    public PaymentChannel Channel { get; set; } // Click / Payme / Cash
    public DateTime PaidAtUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }  // uzaytirilgandan keyingi ExpiresAt
    public Guid AcceptedByUserId { get; set; }  // qaysi SuperAdmin belgiladi
    public string? Note { get; set; }
}
```
Indekslar: `(MarketId, PaidAtUtc desc)` — do'kon kartochkasidagi tarix; `(PaidAtUtc desc)` — «Последние платежи».
Endpointlar: `POST /_sa/markets/{id}/payments` (`[Idempotent]`), `GET /_sa/payments?take=20`, `GET /_sa/markets/{id}/payments`.
Oylik daromad (Панель KPI) = aktiv marketlarning joriy tarif narxlari yig'indisi (MRR), **to'langan summa emas** — dizayndagi «Доход по подпискам · сум/мес» aynan shu.

### BE-S3 — Platforma sozlamalari

Bitta qatorli jadval (`Id = 1`, `ValueGeneratedNever`), `MarketSettings` naqshiga o'xshash, tenant filtri **yo'q**:
`PriceStart/PriceStandard/PricePro`, `LimitUsersStart/Standard/Pro`, `GraceDays` (5), `WarnOnOverdue`/`ReadOnlyAfterGrace`/`FullBlockAfterDays` (30), `SmsExpiring`/`SmsBlocked`/`SmsWelcome`, `SupportPhone`/`SupportTelegram`/`SupportEmail`, `SoonThresholdDays` (7).
`IMemoryCache` bilan keshlanadi (har so'rovda o'qilmasin), yozuvda kesh tozalanadi. Ochiq endpoint `GET /api/public/platform-contacts` faqat 3 kontaktni beradi.

### BE-S4 — Grace period va «только просмотр» ⚠️

Hozirgi qoida (o'zgarmaydi, kengayadi): `IsBlocked → 423`, `ExpiresAt <= now → 402`.
Yangi bosqichlar:

| Kun | Holat | Xatti-harakat |
|---|---|---|
| 0 | Muddat tugadi | Kirish **ochiq**, javobga `X-Subscription-State: overdue` + `expiresAt` → owner/adminda sariq plashka (`WarnOnOverdue`) |
| 1..`GraceDays` | Otsrochka | Xuddi shunday — ish to'xtamaydi |
| >`GraceDays` | **Faqat o'qish** (`ReadOnlyAfterGrace`) | `GET` ✅, `POST/PUT/DELETE` → **402** `SUBSCRIPTION_READONLY` |
| >`FullBlockAfterDays` | To'liq blok | Login ham 402 (hozirgi xulq) |

**Qoidani faqat `Market` entity ichida kengaytirish** — `SubscriptionState Evaluate(DateTime now, PlatformSettings s)`; middleware, login va public endpoint shu bitta metodni chaqiradi (drift bo'lmasin). Frontendda `RequireSubscription` guard'i yangi holatni tushunadi va POS'da yozuv amallarini o'chiradi.
**Testlar majburiy:** har chegara uchun (0 kun / grace ichida / grace+1 / block kuni), GET/POST ajratmasi, SuperAdmin bypass'i.

### BE-S5 — Platforma foydalanuvchilari

- `GET /_sa/users?role=&marketId=&q=&page=` — barcha marketlar bo'ylab (ism, login, telefon, rol, market nomi, oxirgi kirish, holat). Qidiruv server tomonda, sahifalash bilan.
- `POST /_sa/users/{id}/reset-password` `{ newPassword }` — BCrypt hash + `InvalidateSessionsAsync` + audit. **SuperAdmin o'zini o'zgartira olmaydi** (o'z parolini Account orqali).
- `POST /_sa/users/{id}/block|unblock` — `IsActive` flipi + sessiyalarni uzish + audit.
Barchasi `[Authorize(Roles = "SuperAdmin")]` + `_sa` segment gate ostida.

### BE-S6 — Zayavka statusi

`RegistrationRequestStatus` ga `Accepted = 3` qo'shiladi (mavjud qiymatlar tegilmaydi — DB kontrakti). «Подключена» **alohida status emas**: `Status == Approved && CreatedMarketId != null` — ya'ni ma'lumot allaqachon bor, faqat DTO'da hisoblanadi.
`RegistrationRequest.Note` (nullable, ≤300) — welcome forma ixtiyoriy izoh yuborsa. Landing formasiga ham maydon qo'shiladi (ixtiyoriy).
Yangi: `POST /_sa/requests/{id}/accept`, `POST /_sa/requests/{id}/reopen`. `Xmin` optimistik qulfi mavjud — saqlanadi.

### BE-S7 — Bildirishnoma kanali: TELEGRAM (SMS bekor qilindi) ✅

**QAROR (2026-07-26): SMS umuman ishlatilmaydi.** O'rniga loyihada allaqachon
qurilgan Telegram kanali:

| | SMS | Telegram |
|---|---|---|
| Narx | har xabar uchun pul | bepul |
| Yetib borgani | noma'lum | `SendToChatAsync` → `bool` |
| Kanal | bir tomonlama | ikki tomonlama (ega hisobot ham so'raydi) |
| Bog'lanish | telefon raqami (tasdiqlanmagan) | bir martalik kod bilan **tasdiqlangan** |
| Maxfiylik | matn provayder jurnalida | faqat bot va chat |

**Nima yuboriladi:** obuna tugashi (sozlanadigan N kun oldin, bir davrga bitta —
stamp `Market.RenewalReminderSentFor = ExpiresAt`), blok (sababi bilan),
qarzdorlarga qo'lda eslatma (`POST /_sa/reminders/overdue`).

**Yagona kamchiligi va uning yechimi:** ega Telegramni bog'lamagan bo'lsa xabar
yetib bormaydi. Bu **jimgina yo'qolmaydi**: eslatma javobida `unreachable` soni
qaytadi va «Подписки» ro'yxatida «Telegram yo'q» belgisi turadi — operator
o'shalarni qo'ng'iroq bilan xabardor qiladi.

**Do'kon ochilishida** hech qanday kanal kerak emas: operator baribir mijoz
bilan telefonda gaplashadi (oqim: qo'ng'iroq → «Принять» → «Создать магазин»),
manzil/login/parol esa modalda bir marta ko'rsatiladi.

### BE-S11 — Sub-path do'kon nomidan ✅ (bajarildi, 2026-07-26)

**Muammo:** sub-path `GenerateSubdomain(username)` bilan yasalardi — login + tasodifiy 6 belgi (`sardora3f19c`). Mijoz ko'radigan manzil do'kon nomi bilan bog'liq emasdi. Ikkinchi, jiddiyroq nuqson: `char.IsLetterOrDigit` filtri **kirill harflarni ham o'tkazib yuborardi**, avtomatik yo'l esa format tekshiruvidan o'tmasdi — ya'ni kirillcha login uchun DNS-yaroqsiz sub-path yozilib ketardi.

**Yechim:** [SubdomainSlug.cs](../Buildix.Application/Common/SubdomainSlug.cs) — do'kon nomini translitatsiya qiladi va DNS naqshiga keltiradi: «Тош Кон Строй Маркет» → `tosh-kon-stroy-market`, «Стройбаза №1» → `stroybaza-1`. O'zbek kirilli ham (қ→q, ғ→g, ў→o, ҳ→h). Band bo'lsa `-2`, `-3` qo'shiladi; tanlash **tranzaksiya ichida** (`GenerateSubdomainAsync`), oxirgi to'siq — `Markets.Subdomain` unikal indeksi.
`CheckAvailability` endi taklifni **market nomidan** beradi (login'dan emas) — modaldagi jonli preview server yozadigan qiymatning aynan o'zi.
Testlar: [SubdomainSlugTests.cs](../Buildix.Tests/SubdomainSlugTests.cs) — 11 ta (translitatsiya, tinish belgilari, uzun nomni so'z chegarasida kesish, fallback zanjiri).

**Login bog'lanishi tekshirildi — allaqachon to'g'ri:** `/{sub-path}/login` ga faqat **o'sha marketga biriktirilgan** foydalanuvchi kira oladi. [AuthService.Login.cs:96-98](../Buildix.Application/Services/AuthService.Login.cs#L96) slug berilganda nomzodlarni `u.MarketId == slugMarket.Id` bilan cheklaydi; boshqa marketning `sardor`i bu yerda umuman topilmaydi (401). Username **market ichida unikal** (`IX_Users_MarketId_Username_Unique`), ya'ni ikki do'konda bir xil login bo'lishi mumkin va bu ataylab shunday.

---

## 4. Frontend ishlari

| Kod | Ish |
|---|---|
| **FE-S1** | `/_sa/:segment` marshrut daraxti, `SuperAdminLayout`, `RequireRole` guard, segment-aware `superAdminApi` klienti, binafsha tema tokenlari |
| **FE-S2** | Login (`/_sa/:segment/login`) + rol tekshiruvi |
| **FE-S3** | Панель (KPI + 3 panel) |
| **FE-S4** | Заявки (tablar, qidiruv, accept/reject/reopen, «Создать магазин» modali) |
| **FE-S5** | Магазины (chiplar, qidiruv, jadval, **detal drawer**, blok modali) |
| **FE-S6** | Подписки (tarif kartochkalari, jadval, «Оплата получена» modali, to'lovlar tarixi) |
| **FE-S7** | Пользователи (filtrlar, jadval, parol modali + generator) |
| **FE-S8** | Настройки (4 blok, saqlash) + login kontaktlarini sozlamadan olish |

Umumiy: barcha ro'yxatlar **server tomonda** filtrlanadi/sahifalanadi (maketda mock massiv, lekin 100+ do'konda klient filtri yaramaydi). Mavjud `Card`/`Badge`/`Button`/`Toggle`/`Spinner` qayta ishlatiladi.

---

## 5. Bosqichlar

| Bosqich | Mazmun | Natija (acceptance) |
|---|---|---|
| **S0** ✅ | FE-S1, FE-S2 (+BE-S11) | **Bajarildi 2026-07-26.** `/_sa/:segment` marshrut daraxti, `SuperAdminLayout` (binafsha tema `data-theme="super"` orqali), `SuperLoginPage` (slug'siz login + rol tekshiruvi), 6 placeholder sahifa, segment-aware `superAdminApi`, `sa.*` i18n (uz/ru/en). Jonli tekshiruv: to'g'ri segment+token → 200, noto'g'ri segment → 404, tokensiz → 401 |
| **S1** ✅ | BE-S6, BE-S8, FE-S3, FE-S4 | **Bajarildi 2026-07-26:** `Accepted` statusi + `Note`, `accept`/`reopen` endpointlari, 4 tabli ekran, «Создать магазин» modali (login/parol generatori, sub-path jonli preview, muddat tanlash). Jonli sinov: ariza → accept → reopen → accept → do'kon yaratildi (`«Тест Строй Маркет»` → `test-stroy-market`), yangi ega o'z manzilida kirdi, **begona manzilda 401** — keyin test ma'lumoti o'chirildi. **Панель:** `GET /_sa/dashboard` agregati (4 KPI + arizalar + «Требует внимания» + do'konlar), MRR ataylab `null` (tarif S3 da). Testlar: 91/91 |
| **S2** ✅ | BE-S9, FE-S5 | **Bajarildi 2026-07-26.** `Market.City` + migratsiya; do'kon markazidagi `GET /_sa/stores` va `GET /_sa/stores/{id}` (egasi, obuna, xodimlar, shu oydagi cheklar, oxirgi faollik, mijozlar qarzi). Ekran: 4 filtr chipi, qidiruv (nom/egasi/shahar), jadval + **detal drawer** (blok tugmasi oqibat matni bilan). Tarif ustuni `—` (S3). Jonli: ro'yxat 200, detal `checksThisMonth=15`, yo'q do'kon 404. Testlar 97/97 |
| **S3** ✅ | BE-S1, BE-S2, FE-S6 | **Bajarildi 2026-07-26.** `PlanCode` + `PlatformPlans` jadvali (3 qator seed: 600k/1.2M/2.4M) + `Market.Plan`; `SubscriptionPayments` jadvali; `GET /plans`, `/billing`, `/payments`, `/markets/{id}/payment-preview`, `POST /markets/{id}/payments` (**idempotent**). Ekran: 3 tarif kartochkasi, 3 tab, jadval, to'lov modali (natijani oldindan ko'rsatadi), «Последние платежи». Панель MRR va Магазины tarifi jonlandi. Testlar 110/110 (+13 V4 chegaralari). Jonli: takroriy bosish **bitta** to'lov yozdi va javob bayt-ma-bayt bir xil; boshqa tana + o'sha kalit → 422 |
| **S4** ✅ | BE-S5, FE-S7 | **Bajarildi 2026-07-26.** `GET /_sa/users` (rol/do'kon/qidiruv + server sahifalash), `POST /users/{id}/reset-password`, `/block`, `/unblock`. Parol tiklash va blok **sessiyalarni uzadi** (refresh bekor + `TokensInvalidBeforeUtc` + epoch kesh). SuperAdmin konsol orqali tegilmaydi. Ekran: rol chiplari, do'kon select, qidiruv, sahifalash, parol modali (generator, bir martalik ko'rsatish). Testlar 118/118 (+8) |
| **S5** ✅ | BE-S3, BE-S4, FE-S8 | **Bajarildi 2026-07-26.** `PlatformSettings` (bitta qator, seed) + singleton kesh (`IPlatformSettingsProvider`, startupda to'ladi, yozuvda yangilanadi). **B+D siyosati:** `Market.EvaluateSubscription` — Active → Overdue → Restricted → Blocked; middleware `X-Subscription-State` header qo'yadi, `[RequiresActiveSubscription]` faqat sotuv/zakup yozuviga qo'yilgan. Login endi otsrochka va «faqat ko'rish»da OCHIQ. Ekran: 4 blok + saqlash; kirish sahifasi kontaktlari `GET /api/public/support` dan. Jonli 4 bosqich tekshirildi (pastdagi jadval). Testlar 127/127 (+9 chegara) |
| **S6** ✅ | BE-S7 (qayta ko'rildi), BE-S10 | **Bajarildi 2026-07-26 — SMS O'RNIGA TELEGRAM.** Egaga: obuna tugashi (N kun oldin, bir davrga bitta), blok (sababi bilan), «Напомнить всем должникам» → `POST /_sa/reminders/overdue`. Javobda `sent`/`unreachable`, ro'yxatda «Telegram yo'q» belgisi — bog'lamagan egani operator qo'ng'iroq bilan xabardor qiladi. Fon xizmati soatiga bir marta o'tadi (stamp = `ExpiresAt` ning o'zi) |

**Migratsiyalar:** S1 (`Accepted`+`Note`), S2 (`Market.City`), S3 (`Plan` + `SubscriptionPayments`), S5 (`PlatformSettings`), S6 (`SmsOutbox`). Har biri alohida — orqaga qaytarish oson bo'lsin.

**Testlar (majburiy minimum):** zayavka status o'tishlari va `Xmin` poygasi · «Оплата получена» idempotentligi va muddat uzaytirish matematikasi (muddati o'tgan / o'tmagan holat) · grace/read-only chegaralari · parol tiklash sessiyani uzishi · `_sa` endpointlariga Owner/Admin/Seller roli bilan kirish **rad etilishi** (rol matritsasi).

---

## 6. Xavfsizlik eslatmalari

1. **Segment sir emas, faqat qatlam.** Asosiy nazorat — JWT roli. Yangi endpointlarning **har biri** `[Authorize(Roles = "SuperAdmin")]` bilan (kontroller darajasidagi atributga tayanib, alohida `[AllowAnonymous]` qo'ymaslik).
2. **Tenant filtri o'chiq.** §A6 — har query market bo'yicha aniq filtrlanadi yoki ataylab platforma-keng (izoh bilan).
3. **Parol SMS orqali** — BE-S7 dagi ogohlantirishga qarang.
4. **Audit.** Blok, parol tiklash, to'lov qabul qilish, tarif o'zgarishi — hammasi `AuditLogs` ga (mavjud `IAuditLogService`).
5. **Konsolga kirish jurnali.** `LoginHistory` allaqachon yozadi; SuperAdmin kirishlarini alohida ko'rish keyingi bosqich uchun foydali bo'ladi.

---

## 6-bis. Ikki og'ir qaror — variantlar

### Q4 variantlari — «Режим только просмотр»

| # | Variant | Nima o'zgaradi | Xavf | Dizaynga moslik |
|---|---|---|---|---|
| **A** | **Hozirgicha (ikkilik)** | Hech narsa. Muddat tugadi → hamma so'rov 402 | Yo'q | Past — Настройки'dagi 3 toggle o'chiriladi |
| **B** | **Otsrochka + ogohlantirish** | `GraceDays` qo'shiladi: muddat tugagach N kun hamma narsa ishlaydi, faqat sariq plashka; N kundan keyin hozirgi to'liq 402 | Kichik — bitta sanani surish + javob header'i | O'rta — 3 toggle'dan 2 tasi |
| **C** | **To'liq write-ban** | Grace'dan keyin BARCHA `POST/PUT/DELETE` → 402, `GET` ochiq | **Katta** — har bir yozuv yo'lini tasniflash; logout, smena yopish, parol almashtirish kabi "yozuv"lar ham bloklanadi | To'liq |
| **D** ⭐ | **Faqat pul yo'llari bloklanadi** | Grace'dan keyin **sotuv yaratish + zakup qabul** bloklanadi; qolgan hammasi (hisobotlar, qarzlar, smena yopish, eksport) ishlaydi | O'rta-kichik — nuqtali `[RequiresActiveSubscription]` atributi, qo'yilmagan joyga ta'sir qilmaydi | To'liq — dizayn matni **aynan shu**: *«продажи заблокированы, данные видны»* |

**Tavsiya: B + D birga** (S5 bosqichida). Sabab:
- Dizaynning o'z izohi «только просмотр» ni **"продажи заблокированы"** deb ta'riflaydi — global write-ban emas. Ya'ni C — dizayn talab qilmagan qimmat variant.
- D nuqtali: atribut qo'yilmagan endpoint xatti-harakati **umuman o'zgarmaydi** (regressiya maydoni kichik). C esa har bir POST'ni tekshirishga majbur qiladi va bir joyni unutish = do'kon ishlamay qolishi.
- Kassirning yarim qolgan cheki: D'da POS ochiladi, lekin `POST /Sales` 402 beradi — SPA buni ushlab «obuna tugagan» ekranini ko'rsatadi. C'da esa smenani yopish ham imkonsiz bo'lardi (bu ham POST).

**D ning texnik shakli:** `Market.Evaluate(now, settings)` → `SubscriptionState { Active, Overdue, Restricted, Blocked }` (yagona manba — entity ichida). Middleware har javobga `X-Subscription-State` qo'yadi (plashka uchun), `[RequiresActiveSubscription]` atributi esa `Restricted`/`Blocked` da 402 `SUBSCRIPTION_RESTRICTED` qaytaradi. Atribut qo'yiladigan joylar: `POST /Sales`, `POST /Sales/{id}/items`, `POST /Zakups`, `POST /Zakups/{id}/accept`, POS checkout. **Qo'yilmaydi:** auth, smena ochish/yopish, qarz to'lovi qabul qilish (pul kirimi — bloklash mantiqsiz), eksport/hisobot.

### Q6 variantlari — «Оплата получена» matematikasi

Boshlang'ich holat: `ExpiresAt = 28 iyul`, bugun `20 iyul` (erta to'lov) yoki `3 avgust` (6 kun kechikkan).

| # | Qoida | Erta to'lov (20 iyul) | Kechikkan (3 avgust) | Xulosa |
|---|---|---|---|---|
| **V1** | `max(now, ExpiresAt) + 1 oy` | 28 avgust ✅ | 3 sentabr | Kechikkan kunlar bepul; hisob kuni **suriladi** |
| **V2** | `ExpiresAt + 1 oy` (qat'iy kalendar) | 28 avgust ✅ | 28 avgust | Kechikkan davr uchun ham to'lanadi; hisob kuni **qotadi**, lekin 3 oy kechikkan do'kon bitta to'lovdan keyin ham "muddati o'tgan" bo'lib qoladi |
| **V3** | `now + 1 oy` | 20 avgust ❌ | 3 sentabr | Erta to'lagan **kun yo'qotadi** — jazolaydi |
| **V4** ⭐ | **Grace-ga qarab langar:** xizmat uzilmagan bo'lsa (`now - ExpiresAt <= GraceDays`) → `ExpiresAt + N oy`, uzilgan bo'lsa → `now + N oy` | 28 avgust ✅ | grace 5 kun bo'lsa: 6-kun = uzilgan → 3 sentabr | Xizmat ko'rsatilgan davr uchun to'lanadi, ko'rsatilmagani uchun yo'q |

**Tavsiya: V4**, ustiga uchta shart:
1. **Modal natijani oldindan ko'rsatadi** — «Оплачен до: 28 июля → **28 августа**». Operator odam, formulani boshida hisoblab o'tirmasin; ko'rgan narsasi yoziladi.
2. **Davr tanlanadi** — 1 / 3 / 6 / 12 oy (dizaynda faqat oylik, lekin qo'lda to'lovda yillik chegirma odatiy holat).
3. **Idempotent** — mavjud `[Idempotent]` atributi bilan; aks holda ikki marta bosish ikki oy beradi va buni faqat operator payqaydi.

Har uch shart ham «to'lov qabul qilindi» amalini **qaytarib bo'lmaydigan** qilgani uchun kerak: yozilgan `SubscriptionPayment` o'chirilmaydi, faqat teskari yozuv bilan tuzatiladi (audit izchilligi).

---

## 7. Ochiq qarorlar (kod yozishdan oldin)

| # | Savol | Tavsiya |
|---|---|---|
| **Q1** | Tarif limitlari (foydalanuvchi soni) **qattiq bloklaydimi** yoki faqat ogohlantiradimi? | Qattiq blok — aks holda tarif ma'nosini yo'qotadi. Owner'ga «tarifni ko'taring» xabari. |

| **Q3** | To'lov — qo'lda belgilashmi yoki Click/Payme integratsiyasi? | Hozircha **qo'lda** (dizayndagi «Оплата получена» aynan shu). `payOpts` o'lik state'i — hozircha e'tiborsiz. |
| **Q5** | Konsol tili — faqat `ru` yoki 3 tilli? | `ru` birinchi, kalitlar 3 til uchun tayyor (§A4). |

### Hal qilingan qarorlar (2026-07-26)

- ✅ **SMS umuman ishlatilmaydi** (2026-07-26 qarori). Bildirishnoma kanali — **Telegram** (BE-S7). Unutilgan parol → SuperAdmin «Сменить пароль» orqali yangisini yaratadi (BE-S5), eski parol darhol kuchini yo'qotadi va sessiyalar uziladi.
- ✅ **Sub-path do'kon nomidan** yasaladi (BE-S11 — bajarildi).
- ✅ **Sub-path bog'lanishi** — `/{sub-path}/login` ga faqat o'sha marketning foydalanuvchisi kiradi (allaqachon shunday, tekshirildi).
- ✅ **Q4 = B + D** — otsrochka + sariq plashka, grace tugagach faqat **sotuv yaratish va zakup qabul** bloklanadi (`[RequiresActiveSubscription]`). Global write-ban qurilmaydi.
- ✅ **Q6 = V4** — grace-ga qarab langar; modal natijani oldindan ko'rsatadi (`28 июля → 28 августа`), davr tanlanadi (1/3/6/12 oy), amal idempotent.

---

## 8. Nima qilinmaydi (ko'lamdan tashqari)

- «Забыли пароль?» oqimi (dizaynda havola bor, backend yo'q).
- Onlayn to'lov (Click/Payme API) — Q3 ga qarang.
- Welcome-sahifaning o'zi (`Welcome.dc.html` maketlar orasida **yo'q**, lekin barcha fayllar unga havola qiladi; joriy [LandingPage.tsx](../Buildix.Web/src/features/landing/LandingPage.tsx) zayavka yuborishni allaqachon bajaradi).
- Do'kon ichidagi rollar/ruxsatlar — bu Owner/Admin ishi (§2.6).

---

## 9. Ishlab chiqarishga tayyorlik — to'siqlar (2026-07-26, bajarildi)

Chuqur analizda topilgan 4 ta blokerni yopish.

### P1 — SuperAdmin konsoliga oddiy login/parol bilan kirish ✅

Yashirin segment **saqlanib qoladi** (autentifikatsiyagacha 404 — skaner konsol
borligini bilmaydi), lekin operator uni endi qo'lda yozmaydi:

| Qadam | Nima bo'ladi |
|-------|--------------|
| `/login` (yangi marshrut, slug'siz) | Oddiy kirish formasi |
| Login muvaffaqiyatli, rol = `SuperAdmin` | `GET /api/Auth/ConsoleSegment` (`[Authorize(Roles="SuperAdmin")]`) segmentni qaytaradi |
| Frontend | `/_sa/{segment}/dashboard` ga o'tadi |
| Segment sozlanmagan | Formada `auth.errors.consoleNotConfigured` — jimgina yiqilmaydi |
| Do'kon xodimi `/login` dan kirsa | O'z `/{subdomain}` iga yo'naltiriladi |

Allaqachon kirgan SuperAdmin `/login` ga qaytsa, forma emas — to'g'ridan-to'g'ri
konsol ochiladi (segment sessiyada saqlanmaydi, har safar serverdan so'raladi).

Jonli tekshiruv: SuperAdmin → `200 {"segment":"…"}`, anonim → `401`.

### P2 — TLS standart ✅

- `deploy/nginx/default.ssl.conf` — **standart** konfiguratsiya (`.example` emas);
  `NGINX_CONF` bilan almashtiriladi, `default.conf` faqat lokal ishlash uchun.
- `443:443` doim ochiq; `:80` → 301 `https://`, ACME yo'li redirectdan **oldin**.
- Sertifikat yo'q bo'lsa konteyner **yiqilmaydi**: `deploy/nginx/ensure-cert.sh`
  vaqtinchalik o'zi imzolagan sertifikat yaratadi (brauzer ogohlantiradi — bu
  «haqiqiy sertifikat hali qo'yilmagan» degan ko'rinadigan signal).
- `certbot` xizmati `tls` profilida — olish va yangilash `deploy/README.md` §2 da.
- HSTS (`max-age=15552000`) faqat HTTPS blokida: sertifikat muammosi saytni
  brauzerdan butunlay qulflab qo'ymaydi. Qo'shimcha: `nosniff`, `SAMEORIGIN`,
  `Referrer-Policy`.

### P3 — Healthcheck ✅

- `api` — image ichidagi `HEALTHCHECK` (`curl /health`, u DB'ga ping yuboradi).
- `web` — compose healthcheck (`/healthz`) va **`depends_on: api: service_healthy`**:
  deploydan keyingi birinchi so'rov migratsiya qilayotgan upstream'ga tushmaydi.
- `/api/health` proksi qilinadi, lekin **faqat ichki tarmoqdan** (10/172.16/192.168,
  `127.0.0.1`, `::1`) — holat ma'lumoti tashqariga chiqmaydi (tashqaridan 403).

### P4 — `app_logs` cheksiz o'sishi ✅

`LogRetentionBackgroundService`: sutkasiga bir marta, `Logging:RetentionDays`
(standart 30, `0` — o'chiradi) dan eski qatorlarni 5000 talik partiyalarda
o'chiradi (`ctid IN (… LIMIT n)`), bitta o'tishda ko'pi bilan 200 partiya.

Ikki nozik joy jonli sinovda aniqlandi va tuzatildi:

1. Sink `timestamp` (`timestamp without time zone`) ustuniga **jarayonning
   mahalliy** vaqtini yozadi → kesim `DateTime.Now` dan olinadi, `UtcNow` dan emas.
2. `Kind=Local` bo'lgan `DateTime` ni Npgsql `timestamptz` deb hisoblab yiqiladi →
   kesim `DateTimeKind.Unspecified` ga keltiriladi.

### Deploy o'zgaruvchilari ✅

`SUPERADMIN_CONSOLE_SEGMENT` (majburiy), `SUPERADMIN_USERNAME`,
`SUPERADMIN_PASSWORD`, `NGINX_CONF`, `LOG_RETENTION_DAYS` —
`docker-compose.yml`, `.env.example` va `deploy/README.md` da.

CI'ga `deploy-config` job qo'shildi: `docker compose config` + har ikkala nginx
konfiguratsiyasi uchun `nginx -t` (sertifikat skripti bilan birga).
