# Seller (kassir) interfeysini 100% ga yetkazish — reja

**Maqsad:** `docs/Web design seller` (9 maket) va `docs/Web design sellerPNG` (16 skrinshot) dizayniga seller interfeysini **to'liq** moslashtirish — hozir stub/client-side bilan vaqtincha qoplangan yoki umuman yo'q bo'lgan funksiyalarni yopish.

**Manba:** 9 dizayn maketi ekran-ma-ekran o'qildi + `Buildix.Web/src/features/seller/*` (8 sahifa) va `features/account/AccountPage.tsx` joriy holati + `Buildix.API/Controllers`, `Buildix.Application/Services`, `Buildix.Domain/Entities` kod bilan tekshirildi (har da'vo `file:line` bilan). Sana: **2026-07-24**. Bu reja `SELLER-INTEGRATION-PLAN.md` §5b "ochiq qolgan gaplar" ni davom ettiradi.

> **DIQQAT — barcode kiritilmaydi:** Foydalanuvchi qarori bo'yicha **shtrix-kod (barcode) va unga tegishli barcha funksiyalar rejadan chiqarilgan**: `SELLER-INTEGRATION-PLAN.md` dagi **P7/T3** (`Product.Barcode` maydoni, `GET /Products/by-barcode`), POS dagi ajratilgan **skaner tugmasi**, Товары ekranidagi **barcode ustuni/maydoni** — bularning hech biri qilinmaydi. Tovar qidiruvi mavjud holicha (nom/артикул substring) qoladi.

**Umumiy holat:** Seller **funksional ~90%** (kassir real ish kunini yuritadi: POS + Микс checkout + qarzga sotish + chegirma + draftlar + smena ochish/yopish/инкассация/sverka + PDF chek). **Dizayn-spetsifikatsiyasiga ~75%**. Qolgan ish — **2 ta yirik backend-subsistema** (Поставки pipeline, Notifications inbox) + **5 ta o'rta/kichik backend** + shularga bog'liq frontend boyitish.

Belgilar: 🔴 yirik (yangi subsistema/schema) · 🟠 o'rta · 🟡 kichik · ⚪ mayda.

---

## 1. Qolgan gaplar reyestri

| ID | Gap | Og'irlik | Backend | Frontend | Bosqich |
|---|---|---|---|---|---|
| G-1 | **Поставки delivery-pipeline** yo'q — sahifa tekis "получена" ro'yxati, «Начать приёмку» yo'q | 🔴 | ❌ `ZakupReceipt`da lifecycle yo'q ([ZakupService.cs:199](../Buildix.Application/Services/ZakupService.cs#L199)) | ❌ stub ro'yxat | SB4 |
| G-2 | **In-app Notifications** server inbox yo'q — read/unread, «Отметить все», kun-guruh, bell-badge yo'q | 🔴 | ❌ `Notification` entity/controller umuman yo'q; faqat Telegram chiquvchi | ⚠️ client-side yig'ilgan feed | SB3 |
| G-3 | **Tovar detal-paneli** yo'q — min-stok, joy (МЕСТО), supplier, oxirgi kelish, oyiga sotilgan; qator bosilmaydi | 🟠 | ❌ `Product`da maydonlar/stats yo'q ([Product.cs](../Buildix.Domain/Entities/Product.cs)) | ❌ drawer yo'q, МЕСТО ustuni yo'q | SB2 |
| G-4 | **Mijoz** boyitish — filter chiplar, «покупок за месяц» + «последняя» ustunlari, drill-in | 🟠 | ❌ mijoz stats yo'q ([CustomerService.cs](../Buildix.Application/Services/CustomerService.cs)) | ❌ filtrsiz, 3 ustun | SB1 |
| G-5 | **Возвраты** ko'rinishi — «Возвраты» filtri + возвратов kartasi + qaytarish qatorini (manfiy/pushti) ajratish | 🟠 | ⚠️ qaytarish joyida (`ReturnSaleItemAsync` [SaleReversalService.cs:404](../Buildix.Application/Services/SaleReversalService.cs#L404)); ro'yxat/feed yo'q | ❌ filtr/karta/rang yo'q | SB1 |
| G-6 | **«Принятые сегодня»** jadvali yo'q — faqat yig'indi kartasi | 🟡 | ⚠️ `paidToday` yig'indi ([DebtQueryService.cs:158](../Buildix.Application/Services/DebtQueryService.cs#L158)); ro'yxat endpointi yo'q | ❌ jadval yo'q | SB1 |
| G-7 | **Аккаунт** — «Мои результаты · июль» shaxsiy statistika kartasi yo'q | 🟡 | ⚠️ smena stats bor, oylik self-agregat yig'ish kerak | ❌ karta yo'q | SB1 |
| G-8 | **POS mayda** — picker ichida inline «+ Новый клиент»; chekда do'kon nomi/manzil header/footer | 🟡 | ✅ (`createCustomer`, market settings bor) | ❌ picker faqat qidiruv; chek header/footer yo'q | SB5 |
| G-9 | **Seller ruxsatlari** — `CashRegisterAccess`/`UsersShift` seller UI'ga kerakmi, hal qilish | ⚪ | ⚠️ config qarori ([PermissionDefaults.cs:41](../Buildix.Application/Authorization/PermissionDefaults.cs#L41)) | — | SB1 |

*G-1 va G-2 admin-TZ (`ADMIN-DESIGN-INTEGRATION-TZ.md`) BE-5 / BE-4 bilan **aynan bir xil** — bir marta qurilsa, ikkala panelni ham yopadi.*

---

## 2. Bosqichlar

### SB1 — Kichik backend quick-win + bog'liq frontend polish 🟠🟡 (birinchi navbatda)

**Nega birinchi:** kichik, schema-o'zgarishsiz (yoki minimal) endpointlar bir zarbada **4 ta sahifani** (Клиенты, Долги, Мои продажи, Аккаунт) dizaynга yaqinlashtiradi. Momentum + past risk.

| Ish | Fayl | API |
|---|---|---|
| **G-6** Bugungi qabul qilingan qarz-to'lovlar ro'yxati (vaqt/mijoz/usul/summa/qoldiq) | `DebtQueryService.cs`, `DebtsController.cs` — Toshkent-kun helperi allaqachon bor (`:142`) | yangi `GET /Debts/payments/today` |
| **G-6 FE** «Принятые сегодня» jadvali | `features/seller/SellerDebtsPage.tsx` | ↑ |
| **G-4** Mijoz statistikasi (joriy oy: xarid soni + summa; oxirgi xarid sana/summa) | `CustomerService.cs`, `CustomersController.cs` (`Sale` ustidan agregat, schema o'zgarmaydi) | yangi `GET /Customers/{id}/stats` |
| **G-4 FE** filter chiplar (Все/С долгом/Постоянные), «покупок за месяц» + «последняя» ustunlari, qatorga drill-in (detal) | `features/seller/SellerClientsPage.tsx` | ↑ + mavjud `GET /Debts/GetCustomerDebts/{id}` |
| **G-5** Возвраты feed — qaytarishlar ro'yxati (audit `Return` qatorlari allaqachon yoziladi [SaleReversalService.cs:571](../Buildix.Application/Services/SaleReversalService.cs#L571)) | `SaleQueryService.cs` yoki `SaleReversalService.cs`, `SalesController.cs` | yangi `GET /Sales/returns` (yoki `?type=return`) |
| **G-5 FE** «Возвраты» filter pill + возвратов stat kartasi (hozir 4-karta `cashIn`) + qaytarish qatorini manfiy/pushti ajratish | `features/seller/SellerSalesPage.tsx` (`:23` filtr massivi, `:95-100` kartalar) | ↑ |
| **G-7** Seller oylik o'z-natijasi (Продажи/Чеков/Средний/Смен) — smena agregatlari ustidan | `ShiftService.cs`/`SaleQueryService.cs`, self-service (JWT-scoped) | yangi `GET /Sales/my-summary?period=month` yoki `GET /Shifts/my` ni kengaytirish |
| **G-7 FE** «Мои результаты · <oy>» kartasi | `features/account/AccountPage.tsx` (seller ko'rsatilganda) | ↑ |
| **G-9** Seller ruxsatlarini hal qilish — `CashRegisterAccess`/`UsersShift` kerakmi (`sales.delete` **berilmaydi**, dizayn bo'yicha) | `PermissionDefaults.cs:41-57` | config |

**Qabul mezoni:** Клиенты sahifasida filtr + oylik xarid ustunlari ishlaydi va qator bosilib mijoz detali ochiladi; Долги da bugungi qabullar ro'yxatда ko'rinadi; Мои продажи да возврат qatori manfiy/pushti va «Возвраты» filtri ishlaydi; Аккаунт da kassir o'z oylik natijasini ko'radi.

---

### SB2 — Товары detal-paneli + tovar maydonlari 🟠

| Ish | Fayl | API |
|---|---|---|
| **G-3** `Product` ga `WarehouseLocation` (МЕСТО) + ixtiyoriy `SupplierId` maydonlari + migratsiya | `Product.cs`, `Buildix.Infrastructure/Migrations` | — |
| **G-3** Tovar detal-statistikasi: oxirgi kelish (`Zakup`/`ZakupReceipt` dan), oyiga sotilgan (`SaleItem` dan), joriy supplier | `ProductQueryService.cs`, `ProductsController.cs` | yangi `GET /Products/{id}/stats` |
| **G-3 FE** o'ng tomon **detal-drawer** (min-stok, joy, supplier, oxirgi kelish, oyiga sotilgan, «Продать → касса») + jadvalga **МЕСТО** ustuni + qatorni bosiladigan qilish | `features/seller/SellerProductsPage.tsx` (grid hozir 5 ustun) | ↑ |

**Diqqat — barcode YO'Q:** detal-drawerда va jadvalда **shtrix-kod ko'rsatilmaydi**; narх-закупа/наценка kassirга ko'rsatilmaydi (`data.costPrice` RBAC saqlanadi — [ProductMapper.cs:19](../Buildix.Application/Services/ProductMapper.cs#L19)).

**Qabul mezoni:** kassir tovar qatorini bosib, o'ng panelда joy/supplier/oxirgi kelish/oyiga sotilganini ko'radi; МЕСТО ustuni ro'yxatда bor; hech qayerda narх-закупа yoki barcode chiqmaydi.

---

### SB3 — In-app Notifications subsistemasi 🔴 (G-2, admin BE-4 bilan umumiy)

**Nega:** eng ko'rinadigan gap — top-nav qo'ng'irog'ida badge yo'q, feed client-side yig'iladi, read/unread yo'q ([SellerNotificationsPage.tsx](../Buildix.Web/src/features/seller/SellerNotificationsPage.tsx) izohi 24-33; [PermissionDefaults.cs:44](../Buildix.Application/Authorization/PermissionDefaults.cs#L44)).

| Ish | Fayl | API |
|---|---|---|
| Entity **`Notification`** `{ Id, MarketId, UserId?, Category (Warehouse/Debt/Shift/Supply), Severity, Title, Text, ActionTarget, IsRead, CreatedAt }` + migratsiya | yangi `Buildix.Domain/Entities/Notification.cs` | — |
| Generatsiya-triggerlar: kam/tugagan qoldiq, qarz muddati/просрочен, smena yopildi+farq, xarid qabul (SB4 bilan) | domen hodisalariga hook (savdo, qarz, smena, zakup servislari) | — |
| Ro'yxat + o'qish endpointlari | yangi `NotificationsController.cs` + service | `GET /Notifications?category=`, `GET /Notifications/unread-count`, `POST /Notifications/{id}/read`, `POST /Notifications/read-all` |
| **FE** feed'ni serverга ulash: Все/Непрочитанные tab, «Отметить все прочитанными», kun-guruh (СЕГОДНЯ/ВЧЕРА), o'qilmagan nuqta, per-element vaqt+action | `features/seller/SellerNotificationsPage.tsx` | ↑ |
| **FE** top-nav qo'ng'irog'iga o'qilmagan **badge** (hozir badge yo'q — `SellerTopNav.tsx:74-78`) | `app/layouts/SellerTopNav.tsx` | `GET /Notifications/unread-count` |

**Qabul mezoni:** yangi hodisada seller qo'ng'irog'ida raqamli badge chiqadi; feed serverdan keladi, o'qilgach nuqta yo'qoladi, «Отметить все» ishlaydi, kunlar bo'yicha guruhlangan.

---

### SB4 — Поставки delivery-pipeline 🔴 (G-1, admin BE-5 bilan umumiy)

**Nega oxirroqda:** eng yirik va **eng xavfli** — stok-yozuv yo'lini o'zgartiradi (yaratilishни stokka kirimdan ajratadi), shuning uchun tranzaksiya/atomiklik/audit ehtiyot bilan.

| Ish | Fayl | API |
|---|---|---|
| `ZakupReceipt` ga **`DeliveryStatus` (InTransit/Accepted)** + `DriverPhone?` + `ExpectedDate?/ETA?` + migratsiya | `ZakupReceipt.cs`, Migrations | — |
| Yaratishда `InTransit` (**stok o'zgarmaydi**); hozir yaratilishi = darhol stok ([ZakupService.cs:199](../Buildix.Application/Services/ZakupService.cs#L199)) — buni ajratish | `ZakupService.cs` | — |
| **«Начать приёмку» / qabul** → stok + `CostPrice` yangilanadi (tranzaksiya ichida) + audit (+ `StockMovement` agar admin BE-2 qilingan bo'lsa) | `ZakupService.cs`, `ZakupsController.cs` | yangi `POST /Zakups/{id}/accept` (yoki `/start-receiving`) |
| Seller-DTO shaping: yangi pipeline maydonlari sellerга ko'rinadi, summa **ko'rinmaydi** ([ZakupRoleShaper.cs:14](../Buildix.Application/Services/ZakupRoleShaper.cs#L14)) | `ZakupRoleShaper.cs` | — |
| **FE** pipeline kartalar (в пути / ожидает приёмки), status filtri (Все/Ожидаются/Принятые), «Начать приёмку» tugma; hozir tekis "получена" ([SellerSuppliesPage.tsx](../Buildix.Web/src/features/seller/SellerSuppliesPage.tsx) izohi 13-24) | `features/seller/SellerSuppliesPage.tsx` | ↑ |

**Qabul mezoni:** yangi xarid «в пути» sifatida yaratiladi va **stok o'zgarmaydi**; seller «Начать приёмку» bosgach stok va tannarx yangilanadi, status «принята» bo'ladi, audit yoziladi; seller summani ko'rmaydi.

---

### SB5 — POS mayda polish + yakuniy sayqal 🟡⚪

| Ish | Fayl |
|---|---|
| **G-8** Mijoz picker'iga inline «+ Новый клиент (имя + телефон)» | `features/seller/SellerPosPage.tsx` (`CustomerPicker` ~617-683) |
| **G-8** Chop etiladigan chekда do'kon nomi/manzil header + «Спасибо за покупку»/qaytarish siyosati footer | `features/seller/SellerPosPage.tsx` (`ReceiptModal` ~945-1020), market settings dan |
| Qolgan i18n teshiklarини yopish (uchala locale), mayda vizual moslashlar (Аккаунт tenure qatori, admin-kontakt izohi) | `shared/i18n/*`, tegishli sahifalar |
| Responsive/a11y o'tkazish (seller shell `min-w` cheklovsiz) | `app/layouts/SellerLayout.tsx` |

**Qabul mezoni:** kassir mijozни pickerdan chiqmasdan qo'shadi; bosilgan chek do'kon nomi bilan chiqadi; uchala locale toza (`tsc` sinmaydi).

---

## 3. Backendga qo'shiladigan ishlar (jamlanma, barcode-siz)

| # | Ish | Gap | Turi | Hajm |
|---|---|---|---|---|
| SB-BE1 | `GET /Debts/payments/today` — bugungi qabullar ro'yxati | G-6 | endpoint | kichik |
| SB-BE2 | `GET /Customers/{id}/stats` — oylik xarid + oxirgi xarid | G-4 | endpoint | kichik |
| SB-BE3 | `GET /Sales/returns` — qaytarishlar feed'i | G-5 | endpoint | kichik-o'rta |
| SB-BE4 | `GET /Sales/my-summary` — seller oylik o'z-natijasi | G-7 | endpoint | kichik |
| SB-BE5 | `Product.WarehouseLocation` + `SupplierId` + `GET /Products/{id}/stats` | G-3 | migration + endpoint | o'rta |
| SB-BE6 | **`Notification`** entity + triggerlar + 4 endpoint | G-2 | 🔴 yangi subsistema | yirik |
| SB-BE7 | `ZakupReceipt.DeliveryStatus` lifecycle + `POST /Zakups/{id}/accept` + stok-ajratish | G-1 | 🔴 schema + logika | yirik |
| SB-BE8 | Seller ruxsat sozlamasi (`CashRegisterAccess`/`UsersShift` qarori) | G-9 | config | mayda |

**Chiqarilgan (barcode):** ~~`Product.Barcode`~~, ~~`GET /Products/by-barcode`~~ — qilinmaydi.

---

## 4. Tavsiya etilgan tartib

```
SB1 (kichik BE + polish)   ← 4 sahifani bir zarbada dizaynга yaqinlashtiradi, past risk
   ↓
SB2 (Товары detali)        ← tovar boyitiladi (barcode-siz)
   ↓
SB3 (Notifications)        ← eng ko'rinadigan gap; admin bilan umumiy (BE-4)
   ↓
SB4 (Поставки pipeline)    ← eng yirik/xavfli, stok yo'liga tegadi; admin bilan umumiy (BE-5)
   ↓
SB5 (POS polish + sayqal)  ← mayda-chuyda, responsive/a11y
```

Har bosqich mustaqil yetkaziladi. **SB3 va SB4 admin panel bilan umumiy** — agar ikkala panel birga qilinsa, ular bir marta sanaladi.

**Hajm (faqat seller, barcode-siz):** backend ~8–10 kun, frontend ~7 kun, QA+i18n ~2 kun ≈ **~4 ish haftasi solo**. N1+PV1 ni admin bilan birga hisoblasa, seller-eksklyuziv qism (SB1+SB2+SB5) ≈ **~2 hafta**.

---

## 5. "Tayyor" mezoni (har bosqich uchun)

- `npx tsc -b --noEmit` va `npx eslint src` toza (0 warning).
- `dotnet build` + `dotnet test` o'tadi (backend tegilsa) + yangi entity/endpoint uchun test.
- Yangi matnlar **uchala** locale'da (`ru.ts` manba, `uz.ts`/`en.ts` bir xil kalit; aks holda `tsc` sinadi).
- Yangi sahifa/amal ruxsat bilan himoyalangan (`perm(...)` marshrutda + `hasPermission` UI'da) + backend o'sha kalitni tekshiradi.
- Kassir cheklovlari saqlanadi: **narх-закупа/наценка ko'rinmaydi**, ma'lumot «Мои» (o'ziga scope), til-almashtirish yo'q.
- Pul/qoldiqni o'zgartiradigan amallar (qaytarish, приёмка) — tasdiqlash oynasi + audit yozuvi bilan.
- **Atomiklik:** yangi stok/qabul yozuvlari **o'sha operatsiyaning tranzaksiyasi ichida** (`IUnitOfWork.ExecuteInTransactionAsync` + audit bir commit'da) — yarim-yozilgan holat bo'lmasin.
- O'lchov/sana/summa — mavjud helperlar (`unitLabel`, `formatSum`, `formatQty`, Toshkent kuni) orqali.

---

## 6. Ilova — nima ataylab QILINMAYDI

- **Barcode** (P7/T3) va barcha bog'liq funksiyalar — foydalanuvchi qarori.
- **`sales.delete`** seller uchun — dizayn bo'yicha berilmaydi (kassir faqat o'z draftini o'chiradi, cheklangan yo'l orqali — [SaleReversalService.cs:212](../Buildix.Application/Services/SaleReversalService.cs#L212)).
- Dizayndan **ustun** bo'lgan mavjud imkoniyatlar (server-draftlar, Микс, PDF chek, инкассация/sverka, per-tender Терминал/Click, pagination, login tarixi) — o'zgartirilmaydi, saqlanadi.
