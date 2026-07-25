# Admin panel — sayqal/yakunlash rejasi (audit asosida)

**Sana:** 2026-07-25. **Manba:** 17 admin maketi (`docs/Web design admin`) 2 parallel agent tomonidan joriy kod + backend + ruxsat bilan ekran-ma-ekran solishtirildi (har da'vo `file:line`). Bu reja A1–A9 + R1–R4 dan **keyin** qolgan bo'shliqlarni yopadi.

**Holat:** admin **funksional ~95%** (kod dizayndan ko'p joyda oshib ketadi — permission-gating, Excel, invoice PDF, ledgerlar, grafiklar, jonli refetch). **Dizayn-fidelity ~85%.** Farqni: 1 ta haqiqiy backend bo'shlig'i (Клиенты agregatlari), Долги per-check grain, bir nechta FE-polish, 2 tuzatiladigan nomuvofiqlik belgilaydi.

Ekran %: Login 98 · Уведомления 95 · Продажи 95 · Смены 90 · Касса 92 · Склад 90 · Товары 90 · Возвраты 93 · Отчёты 85 · Поставщики 80 · Сотрудники 80 · Панель 80 · Закуп 85 · Новый закуп 80 · Долги 65 · Клиенты 60.

---

## AP0 — Xato/nomuvofiqlik tuzatish 🐛 (tez, birinchi)

| Ish | Fayl | Turi |
|---|---|---|
| **Возвраты ruxsat-kalitini birlashtirish:** `ReturnsPage` create `sales.edit` bilan gated, lekin `SaleDetailModal` va backend `POST /Sales/returns` `sales.return` bilan. → `ReturnsPage`ни `sales.return`ga o'tkazish. | `returns/ReturnsPage.tsx:34` | FE — backend allaqachon `sales.return` |
| **Presetlarга yangi kalitlar:** A6 preset-ро'yxatlari `sales.return`/`zakup.accept`ni o'z ichiga olmaydi → «Старший» presetга `sales.return` (Возвраты, РИСК) qo'shish. `zakup.accept` — ixtiyoriy (kim приёмка qilsa). | `employees/PermissionEditor.tsx:21-33` | FE |

**Qabul mezoni:** admin (owner emas) ReturnsPage'да «Оформить возврат»ни `sales.return` bilan ko'radi; «Старший» preset возврат kalitini beradi.

---

## AP1 — Frontend-polish to'plami 🟢 (backend tayyor, ko'rinarli)

| # | Ish | Ekran | Fayl / manba |
|---|---|---|---|
| AP1.1 | **Закуп qidiruv + qator-o'chirish** — list'ga qidiruv maydoni (номер/поставщик/товар) + qatorга trash (`DELETE /Zakups/{id}` **bor**, `zakup.delete`) + tasdiqlash | Закуп | `purchases/PurchasesPage.tsx` |
| AP1.2 | **Смены jonli subtitle** — «открыта: N · за неделю: M смен · расхождения: X» (`current` + history'dan) | Смены | `shifts/ShiftsPage.tsx:89` |
| AP1.3 | **Поставщики receipt bosiladigan** → order-detal modal (paid/остаток progress). Mavjud `ReceiptDetailModal`ни ulash | Поставщики | `suppliers/SuppliersPage.tsx` `RecentReceipts` |
| AP1.4 | **Сотрудники friendly ruxsat** — xom `{key}` o'rniga tarjima title + hint + **РИСК** badge (price/return) + kartaга capability-chiplar («скидки до 3%», «возвраты», «приёмка») | Сотрудники | `employees/PermissionEditor.tsx:168`, `EmployeesPage.tsx` cards |
| AP1.5 | **Отчёты** — «Чеков/возвратов» KPI qaytarish (hozir Расходы) + sotuvchi progress-bar + «лучший день» highlight | Отчёты | `reports/ReportsPage.tsx` |
| AP1.6 | **Товары «скрыто» count** subtitle'ga; **Касса «на открытии было»** + смена № header (Opening ledger'dan) | Товары/Касса | `products/ProductsPage.tsx`, `cash/CashPage.tsx` |

**Barchasi backend'siz** (mavjud endpoint/ma'lumot). i18n kerak bo'lganlar uchun ru/uz/en.

---

## AP2 — Клиенты xarid-agregatlari 🔴 (eng katta bo'shliq, backend)

Design «ПОКУПОК» + «КУПИЛ ВСЕГО» ustunlari, header «покупали в июле: N», detalда «Последние покупки». Hozir mijoz-scope savdo agregati **yo'q** (`CustomerDetailModal` izohи tasdiqlaydi).

| Ish | Fayl | Turi |
|---|---|---|
| `GetCustomersPaged` javobiga **buysCount + totalBought + lastPurchaseAt** (sahifadagi mijozlar uchun batch, TotalDebt kabi) — *seller S3'да oylik agregat bor, buni umumiy/all-time qilish* | `CustomerService.cs` | endpoint (schema yo'q) |
| Mijoz **xaridlar tarixi**: `GET /Sales?customerId=` yoki `GET /Customers/{id}/purchases` (id·sana·товары·summa·usul) | `SalesController`/`CustomerService` | endpoint |
| **FE:** Клиенты ustunlari (ПОКУПОК/КУПИЛ ВСЕГО) + last-purchase subtitle + header hisoblagichlar; `CustomerDetailModal`га «Последние покупки» + «Все продажи клиента» link | `customers/*` | FE |

**Qabul mezoni:** har mijoz qatorида oylik/jami xarid soni+summa; qator bosilganда oxirgi xaridlar ro'yxati. Klиент ~60% → ~90%.

---

## AP3 — Kichik backend bo'shliqlari 🟠

| Ish | Ekran | Turi |
|---|---|---|
| **Склад «ПОСЛ. ПРИХОД» ustuni** — product-list projeksiyaga `lastReceiptAt`+`lastReceiptNumber` (tovar bo'yicha oxirgi ZakupReceipt). *S4'даgi `GET /Products/{id}/stats` mantig'i list'ga ko'chiriladi.* | Склад | endpoint (schema yo'q) |
| **Новый закуп** — **прибытие sanasi** (`ZakupReceipt.ExpectedDate` **S5'да allaqachon bor!** — faqat CreateReceipt UI'ga date-input) + **to'lov usuli** (Наличные/Перечисление — CreateReceipt DTO'ga `paymentMethod` maydoni) | Новый закуп | kichik (ExpectedDate bor, method yangi) |
| **Аккаунт bitta-sessiya tugatish** — `POST /Auth/RevokeSession/{id}` (hozir faqat RevokeOtherSessions bor) + FE per-row «Завершить» | Аккаунт | endpoint |

---

## AP4 — Долги per-check ko'rinishi 🟠 (design-decision)

Design **chek-darajali** ro'yxat (har chek = qator, o'z muddati/qoldig'i), kod **mijoz-darajали** agregat. Backend per-check'ni qo'llab-quvvatlaydi (`customerDebts`, `pay(debtId)`).

- `DebtsPage`ни chek-darajali qatorlarга o'tkazish (ЧЕК/ТОВАРЫ/СРОК/ОСТАТОК «оплачено X из Y»/«Принять оплату»), status-rangли fon, «несколько долгов» badge.
- Pay/detal modalда: chek line-item jadvali + progress-bar + «После оплаты останется». *«ещё долги» banner PayDebtModal'да allaqachon bor.*
- **FE (backend tayyor).** Долги ~65% → ~90%.

---

## AP5 — Yakuniy sayqal ⚪

- `window.confirm` → styled tasdiqlash modal (Продажи «Аннулировать», Товары «Удалить»).
- **Til Account'да** (dizayn Account'ni so'raydi; hozir Settings'да) — *foydalanuvchining parallel til-refactoringiga bog'liq, u bilan kelishilgach.*
- i18n teshiklari (agar bo'lsa) + a11y.

---

## Ataylab QILINMAYDI / kutilmoqda

| Band | Sabab |
|---|---|
| **§6 admin-kod override** (price/возврат/скидка «только с кодом администратора») | Foydalanuvchi qarori: **v1 = toggle-o'chiq bloklaydi** (kodsiz). Override — kelajakdagi v2. Hech qayerda yo'q (grep tasdiqladi). |
| **Панель inline chek-amallar + 4 toggle** | Joriy qayta talqin (detal-modal + Сотрудники) mantiqan to'g'ri; dizayn maketга literal moslik shart emas. |
| **Новый закуп page vs modal** | TZ modalга ruxsat beradi. |

---

## Tavsiya etilgan tartib
```
AP0 (xato/nomuvofiqlik)  ← tez, poydevor
   ↓
AP1 (FE-polish to'plami) ← backend'siz, ko'p ekranни ko'taradi
   ↓
AP2 (Клиенты agregatlari)← eng katta bo'shliq, 1 endpoint
   ↓
AP3 (kichik BE)          ← Склад/Новый закуп/sessiya
   ↓
AP4 (Долги per-check)    ← grain qayta ishlash
   ↓
AP5 (yakuniy sayqal)
```

## «Tayyor» mezoni (har bosqich)
- `tsc --noEmit` + `eslint src` toza; `dotnet build`+`test` (backend tegilsa) + yangi endpoint uchun test.
- Yangi matn uchala locale'да (ru manba; uz/en parity).
- Ruxsat bilan gate; pul/stok o'zgartiruvchi amal — tasdiqlash + audit.
- Browser (playwright) — 0 console/HTTP xato, dizaynга moslik.
- Commit `Jahongir Saydullayev` nomida, Claude atributsiz.
