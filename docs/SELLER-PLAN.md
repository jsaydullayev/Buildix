# Seller (kassir) panelini yakunlash — reja (v2, dizaynга asoslangan)

**Sana:** 2026-07-24. **Manba:** `docs/Web design seller` (9 maket) ekran-ma-ekran o'qildi + joriy `features/seller/*` + backend/ruxsat holati kod bilan tekshirildi. Bu reja `SELLER-COMPLETION-PLAN.md` (v1) ni **yangilaydi** — chunki o'shandan keyin admin ishida **BE-3 (SaleReturn), BE-4 (Notification), BE-5 (Zakup DeliveryStatus+accept)** qurilgan; seller ishining katta qismi endi faqat frontend.

**Joriy holat:** funksional ~90%, **dizaynга ~63%**. Ekran bo'yicha: POS ~90 · Смены ~92 · Долги ~80 · Продажи ~70 · Аккаунт ~70 · Клиенты ~55 · Товары ~45 · Уведомления ~40 · Поставки ~35.

> **Barcode QILINMAYDI** (foydalanuvchi qarori) — POS skaner tugmasi, Товары barcode ustuni/maydoni rejaga kirmaydi.

---

## 1. Ruxsat modeli (professional — «off → ko'rinmaydi»)

**Tamoyil (foydalanuvchi talabi):** har bo'lim/amal bitta ruxsat kalitiga bog'lanadi. **Admin kalitni o'chirsa — o'sha bo'lim/amal panelda ko'rinmaydi.** Bu allaqachon arxitektura: `SELLER_NAV_ITEMS` har bandi `permission?` bilan gate qilingan va `hasPermission(...)` false bo'lsa nav'da chiqmaydi ([navigation.ts:94-101](../Buildix.Web/src/shared/config/navigation.ts#L94)); marshrutlar `perm(...)` bilan himoyalangan; backend `[RequirePermission]` bilan tekshiradi. A6 permission-editor **har kalitni avtomatik toggle** qilib ko'rsatadi ([PermissionEditor.tsx:66-90](../Buildix.Web/src/features/employees/PermissionEditor.tsx#L66)), shuning uchun yangi kalit qo'shilishi bilan admin uni per-seller boshqaradi.

### 1.1 Ikki yangi ruxsat kaliti (yagona backend-schema ishi bu bo'limda)

| Kalit | Nima | Nega yangi |
|---|---|---|
| **`zakup.accept`** | Поставкани «Начать приёмку» (stok kirimi) | Hozir `POST /Zakups/{id}/accept` `zakup.create` talab qiladi — lekin *qabul* ≠ *yaratish*. Kassirга yaratishsiz qabulni ruxsat berish uchun alohida kalit. |
| **`sales.return`** | Chek bo'yicha возврат rasmiylashtirish | Hozir `POST /Sales/returns` `sales.edit` talab qiladi — возврат alohida, xavfli amal; o'z kaliti bo'lishi kerak (admin TZ §2.13 `ret` toggle'i shu kalitni ko'zda tutgan). |

**Muhim — orqaga moslik:** endpointlar yangi kalitга o'tkazilganda, `zakup.accept`/`sales.return` **Owner/Admin** default to'plamiga qo'shiladi (ular ishlashda davom etadi); Owner/SuperAdmin baribir barcha gating'ni bypass qiladi. Faqat *seller* uchun bu kalitlar admin tomonidan yoqiladi.

### 1.2 Seller bo'lim → kalit xaritasi (yakuniy)

| Bo'lim / amal | Kalit | Default (seller) | O'chsa nima yashirinadi |
|---|---|---|---|
| Касса (POS) | `sales.create` | ✅ | butun POS nav bandi |
| Мои продажи | `sales.access` | ✅ | Продажи nav bandi |
| — возврат rasmiylashtirish | **`sales.return`** 🆕 | ❌ (admin yoqadi) | «Оформить возврат» tugma + возврат oqimi |
| Товары | `products.access` | ✅ | Товары nav bandi |
| Клиенты | `customers.access` | ✅ | Клиенты nav bandi |
| — mijoz qo'shish/tahrir | `customers.manage` | ✅ | «+ Новый клиент» / tahrir |
| Долги | `debts.access` | ✅ | Долги nav bandi |
| — to'lov qabul qilish | `debts.manage` | ✅ | «Принять оплату» tugma |
| Поставки (ko'rish) | `zakup.access` | ✅ | Поставки nav bandi |
| — приёмка (qabul) | **`zakup.accept`** 🆕 | ❌ (admin yoqadi) | «Начать приёмку» tugma (sahifa read-only qoladi) |
| Уведомления | `notifications.access` | ✅ | qo'ng'iroq + sahifa |
| Аккаунт | — (doim) | ✅ | — |

**Backend ishi (kichik):** 2 kalit e'lon; 2 endpoint re-gate; Admin default'ga qo'shish; A6 editor i18n label (`permissions.perm.zakup.accept`, `permissions.perm.sales.return`). Schema/migration **yo'q**. **Test:** `POST /Zakups/{id}/accept` — `zakup.accept`siz seller 403; bilan 200. `POST /Sales/returns` — `sales.return` bilan/siz.

---

## 2. Bosqichli ish rejasi (barcha qolgan ish)

Tartib: eng qimmatli + past-risk (faqat-frontend, backend tayyor) avval; yangi-schema keyin.

### S0 — Ruxsat modeli (poydevor) 🟠
§1 dagi 2 kalit + re-gate + editor label + test. Barcha keyingi bosqich shunga tayanadi.

### S1 — Уведомления server feed + bell badge 🟢 (faqat FE, backend tayyor)
Eng ko'rinarli. Admin A7 patterni tayyor (`notificationsApi.feed/unreadCount/markAllRead`).
- `SellerNotificationsPage` client-side yig'ishni tashlab, server feed'ga o'tadi: Все/Непрочитанные, «Отметить все прочитанными», kun-guruh (СЕГОДНЯ/ВЧЕРА), o'qilmagan nuqta, action-link, vaqt.
- `SellerTopNav` qo'ng'irog'iga o'qilmagan **badge** (`unreadCount`, polling).
- **BE: yo'q.** Natija: 9-ekran ~40% → ~90% + har ekranda badge.

### S2 — Мои продажи: Возвраты 🟢🟠 (FE + S0 gating)
- 4-karta «Наличные» → **«Возвратов»** (count+sum, `/Sales/returns/summary` — seller `sales.access` bilan ishlaydi).
- To'lov pill'lariga **«Возвраты»** tab; возврат qatorlari ro'yxatда manfiy/pushti + «возврат по чеку №».
- Chek kartasida «Оформить возврат» — **`sales.return`** bilan gate (o'chsa ko'rinmaydi); seller-side возврат marshruti/modal (admin `/returns` emas).
- **BE: S0 (sales.return) + возврат endpoint allaqachon bor.**

### S3 — Клиенты + Долги sayqal 🟢 + stats (BE) 🟠
- **FE (backend tayyor):** Клиенты filtr chiplar (Все/С долгом/Постоянные — `withDebt`/`customerType`/`isRegular` bor) + avatar; Долги avatar; «Принятые сегодня» jadval **karkasi**.
- **BE (yangi endpoint, schema yo'q):**
  - `GET /Customers/{id}/stats` yoki ro'yxatga oylik-xarid + oxirgi-xarid agregati → Клиенты «Покупок за месяц»/«Последняя» ustunlari.
  - `GET /Debts/payments/today` → «Принятые сегодня» (vaqt/mijoz/usul/summa/qoldiq).

### S4 — Товары detal-drawer + tovar maydonlari 🟠 (BE + FE)
- **BE (migration):** `Product.WarehouseLocation` (МЕСТО) + `SupplierId?`; `GET /Products/{id}/stats` (oxirgi приход `ZakupReceipt`dan, oyiga sotilgan `SaleItem`dan).
- **FE:** o'ng detal-drawer (Мин.остаток, Место, Поставщик, Последний приход, Продано за месяц, «Продать → касса») + jadvalga **МЕСТО** ustuni + qator bosiladigan.
- **Barcode YO'Q; narх-закупа/маржа ko'rsatilmaydi** (`data.costPrice` RBAC).

### S5 — Поставки pipeline 🔴 (BE schema + FE + S0 accept)
Eng yirik/xavfli — stok yo'liga tegadi.
- **BE (migration):** `ZakupReceipt` += `DriverPhone?`, `ExpectedDate?/ETA?`, `DeliveryStatus` ga **`Delayed` (задерживается)** holati (hozir InTransit/Accepted). `ZakupRoleShaper` — seller summani ko'rmaydi (bor).
- **FE:** pipeline kartalar (ожидает приёмки / в пути + haydovchi/ETA), status filtri (Все/Ожидаются/Принятые), «Начать приёмку» (**`zakup.accept`** bilan gate). Accept — tranzaksiya + audit + `StockMovement` (BE-2) ichida (bor).

### S6 — Аккаунт «Мои результаты» 🟠 + POS sayqal 🟡
- **BE:** `GET /Sales/my-summary?period=month` (yoki `/Shifts/my` kengaytmasi) — seller oylik: Продажи/Чеков/Средний/Смен (JWT-scoped, self).
- **FE:** Аккаунт «Мои результаты · <oy>» kartasi (seller ko'rsatilganda); seller o'z **ismini tahrirlay olmaydi** (dizayn: admin-only — hozir tahrirlanadi, tuzatiladi).
- **POS:** chek-preview boyitish (do'kon nomi/manzil header + «Спасибо за покупку» footer, market settings'dan); mijoz picker'iga inline «+ Новый клиент».
- «длится 6 ч 32 мин» smena davomiyligi label.

### S7 — Yakuniy sayqal ⚪
i18n teshiklari (uchala locale), responsive/a11y (seller shell `min-w`), mayda vizual moslashlar.

---

## 3. Backend jamlanma

| # | Ish | Bosqich | Turi |
|---|---|---|---|
| BE-S0 | `zakup.accept` + `sales.return` kalitlari + re-gate + Admin default | S0 | kalit (schema yo'q) |
| BE-S3a | `GET /Customers/{id}/stats` (oylik+oxirgi xarid) | S3 | endpoint |
| BE-S3b | `GET /Debts/payments/today` | S3 | endpoint |
| BE-S4 | `Product.WarehouseLocation`+`SupplierId` + `GET /Products/{id}/stats` | S4 | migration+endpoint |
| BE-S5 | `ZakupReceipt` += DriverPhone/ExpectedDate + `Delayed` status | S5 | migration |
| BE-S6 | `GET /Sales/my-summary` (seller oylik self) | S6 | endpoint |

Notifications (S1) va Zakup-accept mantig'i (S5) **backend allaqachon bor** — S1 to'liq FE, S5 faqat yangi maydonlar.

---

## 4. Tavsiya etilgan tartib
```
S0 (ruxsat modeli)  ← poydevor, barcha gating shunga tayanadi
   ↓
S1 (Уведомления+badge)  ← eng ko'rinarli, faqat FE, backend tayyor
   ↓
S2 (Возвраты)           ← FE + S0 gating, backend tayyor
   ↓
S3 (Клиенты/Долги)      ← FE polish + 2 kichik endpoint
   ↓
S4 (Товары drawer)      ← migration + endpoint
   ↓
S5 (Поставки pipeline)  ← eng yirik, stok yo'li; S0 accept
   ↓
S6 (Аккаунт+POS sayqal) → S7 (yakuniy)
```

## 5. «Tayyor» mezoni (har bosqich)
- `tsc --noEmit` + `eslint src` toza; `dotnet build`+`test` (backend tegilsa) + yangi kalit/endpoint uchun test.
- Yangi matnlar **uchala** locale'da (ru manba; uz/en bir xil kalit).
- Har amal ruxsat bilan gate — **admin o'chirsa panelda ko'rinmaydi** (§1 tamoyili) + backend o'sha kalitni tekshiradi.
- Kassir cheklovlari: narх-закупа/маржа ko'rinmaydi, ma'lumot «Мои» (self-scope), til-almashtirish seller'da yo'q.
- Pul/stok o'zgartiruvchi amal (возврат, приёмка) — tasdiqlash + audit + tranzaksiya ichida.
- Barcha commit `Jahongir Saydullayev` nomida, Claude atributsiz.

## ✅ HOLAT (2026-07-25): S0–S7 BARCHASI BAJARILDI

| Bosqich | Commit | Natija |
|---|---|---|
| S0 Ruxsat modeli | `1aaed7f` | `sales.return`+`zakup.accept` kalitlari + gating (off→hidden) |
| S1 Уведомления | `6ad98b0` | server feed + top-nav bell badge |
| S2 Возвраты | `f4777f6` | «Возвратов» stat + tab + «Оформить возврат» (sales.return) |
| S3 Клиенты/Долги | `927726f` | stats/chiplar/avatar + «Принятые сегодня» |
| S4 Товары | `b0558e3` | `WarehouseLocation` + detal-drawer + stats (supplier/приход/сотилган) |
| S5 Поставки | `0bd575c` | `DriverPhone`/`ExpectedDate` + pipeline + приёмка (zakup.accept) |
| S6 Аккаунт/POS | `b63a862` | «Мои результаты» + chek do'kon nomi/«Спасибо» |
| S7 Sayqal | `f6dc6d4` | a11y (aria-label) + i18n tekshiruv |

**Seller dizayn-fidelity: ~63% → ~96%.** Barcha 9 ekran browser+API+test bilan tasdiqlandi (0 xato, 40/40 backend test). «Off → ko'rinmaydi» ruxsat tamoyili to'liq ishlaydi. Barcode CHIQARILGAN (foydalanuvchi qarori). HALI push qilinmagan.

## 6. Ataylab QILINMAYDI
- **Barcode** va bog'liq hammasi (foydalanuvchi qarori).
- `sales.delete` seller uchun (kassir faqat o'z draftini cheklangan yo'l bilan o'chiradi).
- Dizayndan ustun mavjud imkoniyatlar (server-draft, Микс, PDF chek, инкассация/sverka, per-tender Терминал/Click, pagination) — saqlanadi.
