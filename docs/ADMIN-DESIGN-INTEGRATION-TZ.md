# Admin panel — yangi dizaynni integratsiya qilish (TZ)

**Maqsad:** `docs/Web design admin` (17 ta `.dc.html` maketi) va `docs/Web design adminPNG` (24 skrinshot) — Owner/Admin paneli uchun **yangi dizayn** to'plamini mavjud `Buildix.Web` admin ilovasiga bosqichma-bosqich integratsiya qilish; dizayn talab qilgan, lekin backend/UI'da yo'q funksiyalarni yopish.

**Manba:** barcha 17 admin maketi ekran-ma-ekran o'qildi + `Buildix.Web/src/features/*`, `app/router.tsx`, `shared/config/navigation.ts` joriy holati + `Buildix.Domain/Entities`, `Buildix.API/Controllers`, `PermissionKeys.cs` bilan solishtirildi. Sana: **2026-07-24**.

> **Tuzatish (2026-07-24, kod bilan qayta tekshirildi):** dastlabki versiyada bir nechta "yo'q" deb belgilangan backend imkoniyati aslida **mavjud** ekan. Tuzatilganlar: **BE-10 (force-close) allaqachon bajarilgan** — ochiq ish emas; **`User.Telegram` allaqachon bor** (BE-9 faqat per-user preferensiyalar qismidan iborat); **Касса deposit/balance bo'laklari mavjud** (§2.3 — yangi ish = ularni tiplangan ledgerga ko'chirish); **narx-override ruxsati `sales.edit`** (`products.edit` emas). Batafsil o'zgargan bandlar quyida belgilangan.

**Umumiy xulosa:** admin panelining ko'p sahifasi (`dashboard`, `sales`, `customers`, `debts`, `purchases`, `suppliers`, `shifts`, `reports`, `employees`, `notifications`, `account`) allaqachon mavjud (`OWNER-GAP-PLAN.md` bosqichlari bajarilgan). Yangi dizayn esa uch narsani talab qiladi:
1. **Navigatsiyani qayta tuzish** — bo'limlarga guruhlangan sidebar + 3 ta yangi menyu bandi.
2. **3 ta butunlay yangi ekran** — **Касса** (naqd ledger), **Товары и цены** (Склад'dan ajratilgan narx boshqaruvi), **Возвраты** (alohida qaytarish jarayoni).
3. **8 ta yangi backend imkoniyati** — stock-movement ledger, naqd ledger (kategoriyali), Notification entity, xarid lifecycle (В пути→Принят), Return modeli, per-user limitlar (chegirma/qarz), davomat (Посещаемость), Telegram bildirishnoma sozlamalari.

Belgilar: 🔴 yangi (ekran yoki backend) · 🟠 muhim moslash · 🟡 o'rta · ⚪ kichik · ✅ mavjud.

---

## 1. Navigatsiya — sidebar qayta tuzilishi

Dizaynda sidebar **bo'limlarga guruhlangan** (hozir tekis ro'yxat). Yangi tuzilma (`shared/config/navigation.ts` + `layouts/Sidebar.tsx`):

| Bo'lim | Bandlar (marshrut) | Holat |
|---|---|---|
| **ОПЕРАЦИИ** | Панель (`dashboard`), Продажи (`sales`), **Касса** (`cash`), Склад (`warehouse`), **Товары и цены** (`products`), **Возвраты** (`returns`) | 3 tasi yangi |
| **КЛИЕНТЫ** | Клиенты (`customers`), Долги (`debts`) | ✅ |
| **СНАБЖЕНИЕ** | Закуп (`purchases`), Поставщики (`suppliers`) | sahifalar ✅, lekin `suppliers` sidebar'da yo'q ⚠️ |
| **УПРАВЛЕНИЕ** | Отчёты (`reports`), Смены (`shifts`), Сотрудники и доступы (`employees`) | ✅ |

**Eslatmalar:**
- Dizaynda **Notifications** sidebar bandi yo'q — u header'dagi qo'ng'iroq (🔔 badge) orqali ochiladi. Joriy `notifications` marshruti qoladi, lekin menyudan olib, header bell'ga bog'lanadi.
- **Account** sidebar footer'idagi foydalanuvchi chipi orqali ochiladi (marshrut o'zgarmaydi).
- Dizaynda **audit** va **settings** alohida bandsiz — audit "журнал действий" sifatida chek amallariga bog'langan. Mavjud `audit`/`settings` marshrutlari **o'chirilmaydi** (backend tayyor), lekin bu dizayn to'plamiga kirmaydi; УПРАВЛЕНИЕ ostida qoldirish mumkin.
- Brend yonida to'q sariq **ADMIN** badge. Sidebar rangi `#0f2557` (mavjud `bg-sidebar` token bilan mos).
- ⚠️ **`suppliers` hozir `NAV_ITEMS`da yo'q** — marshrut ([router.tsx:82](../Buildix.Web/src/app/router.tsx#L82)) bor, lekin sidebarga qo'shilmagan (faqat URL orqali ochiladi). Qayta tuzishda СНАБЖЕНИЕ ostiga **qo'shish** kerak — bu tekis→guruhli ko'chirishning bir qismi.

---

## 2. Ekran-ma-ekran spetsifikatsiya

Har ekran uchun: **dizayn talabi → joriy holat → backend → ish**.

### 2.1 Панель — Dashboard 🟠
**Dizayn:** 4 KPI (Продажи за сегодня, Продавцы на смене, В кассе сейчас, Сигналы склада) · "Все продажи · сегодня" jadvali (ВРЕМЯ/ПРОДАВЕЦ/ТОВАРЫ/ОПЛАТА/СУММА/ДЕЙСТВИЯ) qatoridagi **Изменить чек**/**Удалить чек**→**Вернуть** · "Закупы" jadvali (status: приёмка/в пути/черновик) · o'ng panellar: **Доступы продавцов** (4 toggle), **Сегодня на кассе**, **Требует внимания** (3 signal).
**Joriy:** `dashboard/DashboardPage.tsx` bor — KPI + low-stock/debtor panellar.
**Ish:** dizayn tartibiga moslash. "Доступы продавцов" toggle'lari — kassir ruxsatlarining tez ko'rinishi (§2.13 permission modeliga bog'lanadi). "Изменить/Удалить чек" — Sales bilan bir modal (§2.2). "В кассе сейчас · Касса →" → yangi Касса (§2.3). "Требует внимания" — Notification'lardan (§2.14) yig'iladi.
**Backend:** bugungi savdo aggregati, ochiq smena/kassir, kassa qoldig'i (§2.3), kam qoldiq signallari — `GET /Reports/dashboard` mavjud, kassa qoldig'i qismi yangi.

### 2.2 Продажи — Sales 🟠
**Dizayn:** header period toggle (**Сегодня/Вчера/Все**) · qidiruv (chek№/tovar/mijoz) · sotuvchi chiplari (**Все/har sotuvchi**) · jadval (ЧЕК/ПРОДАВЕЦ/ТОВАРЫ/ОПЛАТА/СУММА) · to'lov pill (Наличные/Карта/В долг/Смешанная) · qator→detal modal · **Аннулировать чек** (qizil tasdiq: "омборга qaytadi, выручкадан ayriladi, журналга yoziladi").
**Joriy:** `sales/SalesPage.tsx` + `SaleDetailModal.tsx` bor (void + return-item allaqachon mavjud — `OWNER-GAP-PLAN` B4 bajarilgan).
**Ish:** period toggle (Сегодня/Вчера/Все), sotuvchi filtri chiplari (`sellerId` param), qidiruvni to'liq (chek№/tovar/mijoz) qilib moslash; void modal matnini dizaynga keltirish.
**Backend:** ✅ `GET /Sales?period=&sellerId=&search=`, `POST /Sales/{id}/cancel`.

### 2.3 Касса — Cash 🔴 (YANGI EKRAN + BACKEND)
**Dizayn:** naqd pul **ledgeri** (joriy kun/smena) — 3 KPI (**Сейчас в кассе** / **Приход за день** +/ **Расход за день** −) · filtr (Все/Приход/Расход) · jadval (ВРЕМЯ/ТИП/ОПИСАНИЕ/КТО/СУММА±) · **Новая операция** modal.
Operatsiya turlari (ТИП): **Продажа**, **Оплата долга**, **Внесение**, **Расход**, **Инкассация**, **Открытие**. Savdo va qarz to'lovlari **avtomatik** kiradi; Расход/Инкассация/Внесение qo'lda. Расход uchun **kategoriya**: Хозяйственные/Доставка/Аванс сотруднику/Прочее. Qoldiqdan katta chiqim bloklanadi ("Сумма больше остатка").
**Joriy:** ❌ alohida Касса sahifasi yo'q — faqat `shifts` bor. Backend'da **bo'laklar allaqachon mavjud, lekin tarqoq va tiplanmagan:**
- ✅ **Внесение** — `POST /CashRegister/add` (`AddCash`, `cashregister.manage`, `[Idempotent]`, auditli) allaqachon bor.
- ✅ **Расход/Инкассация** — `POST /CashRegister/withdraw` + `withdraw-request`/`approve`/`reject` + `WithdrawalApprovalStatus`.
- ✅ **Kassa qoldig'i** — `GET /Reports/cash-balance` (`data.cashBalance`) allaqachon hisoblaydi.
- ❌ **Yo'q** — bularni birlashtiruvchi **yagona tiplangan ledger**: `Type`/kategoriya, savdo va qarz to'lovining **avto-kirimi**, va **harakatlar ro'yxati** (`GET /movements?date=`).
**Backend (yangi — mavjudini birlashtirish, noldan emas):**
- Yangi entity **`CashMovement`**: `{ Id, MarketId, ShiftId, Type (Sale/DebtPayment/Deposit/Expense/Collection/Opening), Amount(±), ExpenseCategory?, Comment, UserId, CreatedAt }`. Mavjud `AddCash`/`Withdraw` operatsiyalari shu ledgerga **ko'chiriladi** (bir yozuv ikki joyda bo'lmasin).
- Savdo (naqd) va qarz to'lovi ledgerga **avtomatik** yoziladi — SalePaymentService/DebtService ichida, **o'sha operatsiyaning tranzaksiyasi ichida** (§5 atomiklik).
- `GET /CashRegister/movements?date=` (yangi), `POST /CashRegister/movements` (Расход/Инкассация/Внесение — mavjud `add`/`withdraw` ustiga yoki o'rniga), qoldiq = harakatlar yig'indisi (`cash-balance`ni shu ledgerga tayanadigan qilish), chiqimda overdraft tekshiruvi.
- Ruxsat: `cashregister.access` (ko'rish), `cashregister.manage` (operatsiya) — ✅ ikkalasi ham mavjud.
**Ish:** yangi `features/cash/CashPage.tsx` + `NewCashOperationModal.tsx` + `cash/api.ts`, `cash` marshruti, menyu bandi.

### 2.4 Склад — Warehouse 🟠
**Dizayn:** faqat **qoldiq va harakat** — subtitle (позиций/мало/нет) · **+ Оформить закуп** (→ Purchase Create) · status filtri (Все/В норме/Мало/Нет) · jadval (ТОВАР/ОСТАТОК/**МИН. ОСТАТОК** inline/СТАТУС/ПОСЛ. ПРИХОД) · qator→**harakat ledgeri** modal (Продажа·Ч-#/Приход·З-#/Корректировка) + **Корректировка (инвентаризация)** (fakt qoldiq → farq log'ga yoziladi).
**Joriy:** `warehouse/WarehousePage.tsx` bor, lekin narx + qoldiq **birga** (dizayn ularni ajratadi). `StocktakeModal` bor.
**Backend (yangi):** **StockMovement ledger yo'q** → yangi entity **`StockMovement`** `{ ProductId, Type (Sale/Purchase/Correction), RefId (SaleId/ZakupReceiptId), Delta, ResultingQty, UserId, CreatedAt }`; SaleItem/Zakup/Stocktake yaratilганда yoziladi. `GET /Products/{id}/movements`. Inline min-stock → `PATCH /Products/{id}` (`MinThreshold` mavjud). Inventarizatsiya → mavjud stocktake harakatni yozadi.
**Ish:** Warehouse'ni **faqat qoldiq/harakat** ga qisqartirish; narx qismini Товары ekraniga (§2.5) ko'chirish; harakat modalини qo'shish.

### 2.5 Товары и цены — Products 🔴 (EKRAN AJRATISH)
**Dizayn:** katalog + **narx** boshqaruvi — subtitle (товаров/скрыто) · **+ Добавить товар** · kategoriya chiplari · jadval (ТОВАР/ОСТАТОК/**ЦЕНА ЗАКУПА** read-only/**ЦЕНА ПРОДАЖИ** inline/**МАРЖА** hisob·rangli/action) · **Скрыть/Показать** (POS'dan yashirish) · qator→**Описание** (sotuvchiga ko'rinadi) + O'chirish. Add modal: Название*, Категория, Единица, Цена закупа, Цена продажи*, jonli Маржа.
**Joriy:** ❌ alohida "Товары" yo'q — `warehouse` ichida. `ProductFormModal` + `CategoriesModal` bor.
**Backend:** `Product`da bor: `CostPrice`, `SalePrice`, `MinSalePrice`, `Sku`, `ImageUrl`, `HidePriceFromSellers`, `MinThreshold`, `Unit`, `Category`. **Yo'q:** `Description` (yangi maydon) va **POS'dan yashirish** flagi — dizayndagi "Скрыть" = katalogdan/kassadan yashirish, bu `HidePriceFromSellers` (narxni yashirish) dan **boshqa**. Yangi `IsHidden` (yoki `IsVisibleOnPos`) flagi kerak. Inline narx → `PATCH /Products/{id}/price`. `data.costPrice` ruxsati "Цена закупа/Маржа" ko'rinishini boshqaradi.
**Ish:** yangi `features/products/ProductsPage.tsx` (yoki `warehouse`ni ikkiga bo'lish) + `products` marshruti + menyu; `Product`ga `Description` + `IsHidden` migration; inline price-patch. **Eslatma:** dizaynда SKU/shtrix-kod, rasm, Excel import **yo'q** — mavjudlarini qoldiramiz, lekin bu ekranда ko'rsatmaymiz.

### 2.6 Возвраты — Returns 🔴 (YANGI EKRAN + BACKEND)
**Dizayn:** qaytarishlar ro'yxati + yangi qaytarish jarayoni — subtitle (oyiga N ta, summa, % выручки) · **Оформить возврат** · qidiruv · sabab chiplari (**Все/Брак/Не подошёл/Ошибка продавца**) · jadval (ВОЗВРАТ/ЧЕК/ТОВАРЫ/**ПРИЧИНА**/**ДЕНЬГИ**/СУММА «−») · qator→detal. **Оформить возврат** modal: chek № qidiruv (autocomplete) → chek qatorlari checklist + **qisman miqdor** input (0…sotilgan) → sabab → **Вернуть деньги** (Наличные/На карту) → "К возврату: X".
**Joriy:** ❌ alohida Returns yo'q — qaytarish `SaleDetailModal` ichidagi `return-item` orqali (sababsiz, refund-usulsiz, ro'yxatsiz).
**Backend (yangi):** qaytarishни alohida yozuvга aylantirish — entity **`SaleReturn`** `{ Id, MarketId, SaleId, Number (В-##), Reason (Defect/NotFit/SellerError), RefundMethod (Cash/Card/Transfer), UserId, CreatedAt, Items[] }`. Yaratishда: qoldiqqa qaytarish + StockMovement (§2.4) + kassadan chiqim (§2.3) + audit + выручкадан ayirish. `GET /Sales/returns?reason=&search=`, `POST /Sales/returns` (chek + qatorlar + qisman miqdor). Oylik yig'indi + % выручки.
**Ish:** yangi `features/returns/ReturnsPage.tsx` + `NewReturnModal.tsx` + `ReturnDetailModal.tsx` + `returns` marshruti + menyu. Mavjud `return-item` mantig'ini yangi endpointga migratsiya.

### 2.7 Клиенты — Clients ✅🟡
**Dizayn:** subtitle (клиентов/с долгом/покупали) · **Добавить клиента** · qidiruv + chiplar (Все/С долгом/Организации) · jadval (КЛИЕНТ/ТЕЛЕФОН/ПОКУПОК/КУПИЛ ВСЕГО/ДОЛГ) · qator→detal (3 stat + "Последние покупки" + "Принять оплату долга"/"Все продажи клиента"). Add modal: **faqat** Имя*, Телефон*, Тип (Частное лицо/Организация).
**Joriy:** `customers/CustomersPage.tsx` + `CustomerFormModal.tsx` + `CustomerDetailModal.tsx` bor.
**Ish:** dizaynга moslash. **Diqqat:** dizayn add formida qarz limiti va "doimiy mijoz" **yo'q** — agar mavjud formда bor bo'lsa, dizaynга qarab soddalashtirish yoki kelishilgan holda qoldirish.
**Backend:** ✅ `CustomersController` (CRUD), aggregatlar, `GET /Debts/GetCustomerDebts/{id}`.

### 2.8 Долги — Debts ✅🟡
**Dizayn:** **chek darajasidagi** qarz (har chek — alohida muddat/qoldiq, to'lovlar birlashmaydi) · subtitle (долгов/клиентов/всего/просрочено) · chiplar (Все/Просрочены/Срок сегодня) · qidiruv (клиент/телефон/чек№) · jadval (КЛИЕНТ/ЧЕК/ТОВАРЫ/СРОК/ОСТАТОК ДОЛГА/Принять оплату) · detal modal: qatorlar + progress + "Принять оплату по этому чеку" (Весь остаток, Наличные/Карта, jonli qoldiq).
**Joriy:** `debts/DebtsPage.tsx` + `PayDebtModal.tsx` bor.
**Ish:** dizaynга moslash (chek-darajali ko'rinish, "У клиента есть ещё долги" banneri). **Eslatma:** dizaynда muddatni o'zgartirish **yo'q** (display-only) — `debts.dueDate` endpointi bor, lekin bu ekranда shart emas. To'lov kassaга kirim sifatida yoziladi (§2.3).
**Backend:** ✅ `DebtsController`, chek-darajali `Debt` modeli mos.

### 2.9 Закуп — Purchases 🟠
**Dizayn:** subtitle (закупов/в пути/долг поставщикам) · **+ Создать закуп** · status tab (Все/**В пути**/**Принят**) + qidiruv · jadval (НОМЕР/ПОСТАВЩИК/ТОВАРЫ/СУММА+долг/**СТАТУС**/delete) · detal modal: qatorlar + to'lov holati + **"Отметить принятым"** (В пути→Принят, omborга kirim).
**Joriy:** `purchases/PurchasesPage.tsx` + `NewPurchaseModal.tsx` + `ReceiptDetailModal.tsx` + `SupplierFormModal.tsx` bor (`OWNER-GAP-PLAN` B1 bajarilgan). **Lekin** hozir Zakup **darhol** omborга kiradi.
**Backend (yangi — lifecycle):** `ZakupReceipt`ga **`DeliveryStatus` (InTransit/Accepted)** qo'shish. Yaratishда `InTransit` (stok o'zgarmaydi), `POST /Zakups/{id}/accept` → stok + `CostPrice` yangilanadi + StockMovement (§2.4). Bu `SELLER-INTEGRATION-PLAN` "Поставки" pipeline'ni ham yopadi (sotuvchi "В пути" ko'radi).
**Ish:** status tab + "Отметить принятым" tugma; NewPurchaseModal → to'liq sahifa (§2.10) ga o'tkazish (ixtiyoriy).

### 2.10 Новый закуп — Purchase Create 🟠
**Dizayn:** **to'liq sahifa** — Card1 Поставщик+Когда прибудет; Card2 tovar qidiruv (autocomplete "остаток: X")+qatorlar (ТОВАР/КОЛ-ВО/ЦЕНА ЗАКУПА prefill/СУММА); Card3 Оплата (Оплачиваем сейчас+Вся сумма, Наличные/Перечисление, "Остаётся долгом"); Card4 **quick-add chiplar** (kam qolgan tovarlar); submit validatsiya.
**Joriy:** `NewPurchaseModal.tsx` (modal, sahifa emas).
**Ish:** modalни dizayndagidek to'liq sahifaga (`purchases/new`) yoyish yoki modalda saqlab qatlamlarни dizaynga keltirish; quick-add (kam qolgan tovar) chiplari; oxirgi xarid narxidan prefill.
**Backend:** ✅ `POST /Zakups` (lifecycle bilan §2.9), kam qoldiq ro'yxati (`GET /Products/summary` — `OWNER-GAP-PLAN` O-15).

### 2.11 Поставщики — Suppliers 🟠
**Dizayn:** **master-detail** (ikki ustun) — chap: qidiruv + supplier kartalar (nom/kategoriya/закупов/долг yoki "нет долга"); o'ng (sticky): tanlangan supplier — Телефон/Контакт/Срок доставки/Долг + **Погасить долг** + "Последние закупы". Pay-debt modal: Сумма+Весь долг, Наличные/Перечисление (**FIFO** — eski buyurtmadan yopadi). Add modal: **faqat** Название*, Телефон*, Контактное лицо.
**Joriy:** `suppliers/SuppliersPage.tsx` bor (`OWNER-GAP-PLAN` B1).
**Ish:** master-detail layout'ga moslash; FIFO to'lov taqsimoti; order-detail progress bar.
**Backend:** ✅ `SuppliersController` (CRUD), `POST /Zakups/{id}` (to'lov). FIFO taqsimotни server tasdiqlashi kerak.

### 2.12 Отчёты — Reports 🟠
**Dizayn:** period toggle (**Неделя/Месяц**) hamma narsani qayta hisoblaydi · 4 KPI (Выручка+delta/**Прибыль**+маржа/Чеков+возвратов/Средний чек) · "Выручка по дням" bar (+прогноз) · "Способы оплаты" (Наличные/Карта/В долг + %, >20% qarz ogohlantirishi) · "Топ товаров" (01–05) · "Продавцы" (чеков/ср.чек/summa/ulush %).
**Joriy:** `reports/ReportsPage.tsx` bor.
**Ish:** dizayn tarkibiga moslash — foyda/marja, to'lov taqsimoti + qarz riski, top tovarlar, sotuvchi reytingi, hafta/oy toggle, davr-ustma-davr delta.
**Backend:** ✅ `Reports/*` (dashboard, sales, financial) — foyda `data.profit` ruxsatiga bog'liq. Прогноз/delta qismlari yangi bo'lishi mumkin.

### 2.13 Сотрудники и доступы — Employees 🔴 (RUXSAT MODELI)
**Dizayn:** master-detail — chap: xodim kartalari (avatar/status/telefon/логин/oylik savdo/**profil chiplari**); o'ng (sticky): **ruxsat muharriri**.
Ruxsat modeli — **preset + limit + toggle** (backend'dagi tekis kalitlardan farqli):
- **ПРОФИЛЬ ДОСТУПА:** Стажёр / Продавец / Старший / «Свой набор» (custom).
- **КАССА:** `hold` Отложенные чеки; `disc` Скидки → **Лимит скидки 3%/5%/10%**; `price` Изменение цены (РИСК).
- **КЛИЕНТЫ И ДОЛГИ:** `debt` Продажа в долг → **Долг на чек 5млн/20млн/Без лимита**; `payin` Приём оплаты долга; `ret` Возвраты (РИСК).
- **СКЛАД:** `supply` Приёмка поставок.
- Amallar: **Сбросить пароль**, **Заблокировать/Разблокировать**. Audit footer ("вступают в силу при следующем входе").
Presetlar: Стажёр (faqat hold) / Продавец (hold,disc3,debt5,payin,supply) / Старший (hammasi, disc10, debtбез лимита, price, ret).
**Joriy:** `employees/*` + `PermissionsModal.tsx` bor, lekin **tekis `PermissionKeys`** ishlatadi (preset/limit yo'q).
**Backend:** granular `PermissionKeys` bor (`sales.*`, `products.*`, `data.*`...). **Yo'q:** per-user **chegirma limiti %** va **qarz limiti summa**. Ikki yangi maydon: `User.MaxDiscountPercent`, `User.MaxDebtPerCheck` (null=cheksiz). Dizayn toggle'lari kalitlarga xaritalanadi:
| Dizayn | Backend kaliti |
|---|---|
| hold (отлож. чеки) | `sales.hold` (yangi kalit yoki mavjud draft ruxsati — hozir kassir `sales.create` bilan draftni park qiladi) |
| disc + discLim | `sales.discount` (yangi) + `User.MaxDiscountPercent` |
| price | **`sales.edit`** — sotuv ichidagi narx-override endpointi `PATCH /Sales/items/price` shu kalit bilan himoyalangan (`products.edit` emas) |
| debt + debtLim | `sales.create` (долг — `POST /Sales/{id}/mark-debt` `sales.create` ostiga o'tkazilgan) + `User.MaxDebtPerCheck` |
| payin | `debts.manage` |
| ret | `sales.return` (yangi) |
| supply | `zakup.access`/приёмка (§2.9 accept) |

**Diqqat — dizayn nozikligi:** «Изменение цены» va «Возвраты» toggle'lari o'chirilganda dizayn *«только с кодом администратора»* deb yozadi — ya'ni ruxsatsiz kassir **admin-kod** kiritib bir martalik amalga ruxsat olishi mumkin. Bu tekis on/off ruxsatdan farqli **override-kod** mexanizmi; hozircha modelda yo'q — §6 ochiq savoliga qo'shildi.
**Ish:** `PermissionsModal`ни preset + limit + guruhlangan toggle modeliga qayta yozish; `User`ga ikki limit maydoni (migration); server preset↔kalit xaritasi + "custom" aniqlash. **Diqqat:** Owner o'z `users.manage`ini yo'qotmasin; rol o'zgartirish faqat Owner'da.

### 2.14 Уведомления — Notifications 🔴 (BACKEND ENTITY)
**Dizayn:** kun bo'yicha guruhlangan feed (СЕГОДНЯ/ВЧЕРА) · kategoriya tablari (Все/Склад/Долги/Смены/Поставки) · **Отметить все прочитанными** · element: ikon+tag+**o'qilmagan nuqta**+matn+harakat linki+vaqt. Turlar: kam/tugagan qoldiq, kassa farqi, smena yopildi, qarz muddati/просрочен, xarid qabul/в пути. Rang: qizil/sariq/yashil/ko'k.
**Joriy:** `notifications/NotificationsPage.tsx` bor, lekin (seller'dagidek) **client'da yig'iladi** — server Notification entity yo'q.
**Backend (yangi):** entity **`Notification`** `{ Id, MarketId, UserId?, Category (Warehouse/Debt/Shift/Supply), Severity, Title, Text, ActionTarget, IsRead, CreatedAt }`; domen hodisalari (stok chegarasi, qarz muddati, smena yopilishi+farq, xarid qabul) trigger qiladi. `GET /Notifications?category=`, `POST /Notifications/read-all`, `POST /Notifications/{id}/read`, o'qilmagan hisoblagich. Bu `SELLER-INTEGRATION-PLAN` §3.4 N1 ni ham yopadi.
**Ish:** entity + generatorlar + endpointlar; front feed'ni serverga ulash; header bell badge.

### 2.15 Смены — Shifts 🟠 (+ Посещаемость tab)
**Dizayn:** ikki tab — **Журнал смен** (СМЕНА/ПРОДАВЕЦ/ВРЕМЯ/ЧЕКОВ/ВЫРУЧКА/**КАССА сходится|±**/СТАТУС; detal: **per-tender** Наличные/Карта/В долг + sverka) va **Посещаемость** (СОТРУДНИК/СМЕН/ДНЕЙ/ЧАСОВ/СР.СМЕНА/**ОПОЗДАНИЯ**/ВЫПОЛНЕНИЕ ПЛАНА). Kech = 08:15 dan keyin ochilish; ish soati smena vaqtidan.
**Joriy:** `shifts/ShiftsPage.tsx` + `CloseShiftModal.tsx` + `WithdrawModal.tsx` bor.
**Ish:** Журнал tab'ni per-tender sverka bilan moslash; **Посещаемость tab — yangi** (smena vaqtlaridan hisoblanadi). Dizaynда smena detali view-only (force-close tugma yo'q).
**Backend:** `Shift` (raqamli, per-tender) bor. Davomat — smena open/close vaqtidan **hisoblanadi** (yangi `GET /Shifts/attendance?period=`, lateness 08:15 + reja soatlari). **Force-close ✅ ALLAQACHON BAJARILGAN** — `ShiftService.ForceCloseShiftAsync` + `POST /Shifts/{shiftId}/force-close` [`UsersShift`] + frontend `shiftsApi.forceClose` mavjud ([ShiftsController.cs:90](../Buildix.API/Controllers/ShiftsController.cs#L90)). BE-10 endi ochiq ish emas; qolgani — Смены sahifasiga tugma ulash (agar hali qilinmagan bo'lsa).

### 2.16 Мой аккаунт — Account 🟡
**Dizayn:** chap — Profil (Имя, Телефон, **Логин** read-only, **Telegram**), Смена пароля (тек/новый≥8/повтор); o'ng — **Язык интерфейса** (Oʻzbekcha/Русский/English), **Уведомления в Telegram** (Просроченные долги/Товар закончился/Закрытие смены toggle), **Активные сессии** (qurilma/brauzer/oxirgi + "Завершить").
**Joriy:** `account/AccountPage.tsx` bor (profil, til, sessiyalar).
**Ish:** Telegram username maydoni (**backend'da bor**, faqat UI kerak) + **per-user Telegram bildirishnoma sozlamalari** (yangi) qo'shish.
**Backend:** profil update, parol, til ✅. **`User.Telegram` allaqachon mavjud** ([User.cs:18-19](../Buildix.Domain/Entities/User.cs#L18)) — bu maydon YANGI EMAS, faqat frontendда ko'rsatilmagan. Sessiyalar: `LoginHistory` (DeviceInfo/IpAddress) bor; "активные сессии + завершить" — RefreshToken'lardan ro'yxat + bekor qilish. **Yangi (faqat shu):** `User`da `{debt, stock, shift}` **per-user** bildirishnoma preferensiyalari. Namuna sifatida **market darajasidagi preferensiyalar allaqachon bor** ([MarketSettings.cs:58-67](../Buildix.Domain/Entities/MarketSettings.cs#L58): `NotifyDaySummary`/`NotifyOverdueDebts`/`NotifyWithdrawalRequests`/`OwnerTelegram`/`OwnerTelegramChatId`) — per-user variant shular andozasida, `TelegramNotifier`ga ulanadi.

### 2.17 Login ✅
**Dizayn:** markazlashgan forma (Логин/Пароль/Войти) + til switch + "Связь с администратором" (tel/email/telegram). Mavjud `auth/LoginPage.tsx` bilan mos — kichik vizual moslash.

---

## 3. Backendga qo'shiladigan ishlar (jamlanma)

| # | Ish | Ekran | Turi |
|---|---|---|---|
| BE-1 | **`CashMovement`** ledger (tiplangan: Sale/DebtPayment/Deposit/Expense/Collection/Opening + kategoriya) + avto-kirim + overdraft. *Mavjud `add`/`withdraw`/`cash-balance`ni birlashtiradi, noldan emas.* | Касса (§2.3) | 🔴 yangi entity |
| BE-2 | **`StockMovement`** ledger (Sale/Purchase/Correction + delta + resulting) | Склад (§2.4) | 🔴 yangi entity |
| BE-3 | **`SaleReturn`** modeli (reason + refund method + qatorlar) + `POST /Sales/returns` | Возвраты (§2.6) | 🔴 yangi entity |
| BE-4 | **`Notification`** entity + generatorlar + read-state | Уведомления (§2.14) | 🔴 yangi entity |
| BE-5 | Zakup **`DeliveryStatus`** (InTransit/Accepted) + `POST /Zakups/{id}/accept` | Закуп (§2.9) | 🔴 lifecycle |
| BE-6 | `User.MaxDiscountPercent` + `User.MaxDebtPerCheck` + preset↔kalit xaritasi | Сотрудники (§2.13) | 🟠 migration |
| BE-7 | `Product.Description` + `Product.IsHidden` (POS visibility) + `PATCH /Products/{id}/price` | Товары (§2.5) | 🟠 migration |
| BE-8 | `GET /Shifts/attendance` (davomat: soat/kechikish/reja) | Смены (§2.15) | 🟠 hisob |
| BE-9 | `User`ga **per-user** bildirishnoma preferensiyalari `{debt,stock,shift}`. *`User.Telegram` allaqachon bor — u kiritilmaydi.* | Account (§2.16) | 🟡 migration |
| ~~BE-10~~ | ~~`POST /Shifts/{id}/force-close`~~ — ✅ **BAJARILGAN** (`ForceCloseShiftAsync` + endpoint + `shiftsApi.forceClose`). Ochiq ish emas. | Смены (§2.15) | ✅ done |
| BE-11 | Aktiv sessiyalar ro'yxati + bekor qilish (RefreshToken) | Account (§2.16) | 🟡 |

Qolgan barcha ish — frontend.

---

## 4. Bosqichlar (tavsiya etilgan tartib)

```
A1  Navigatsiya qayta tuzilishi (sidebar guruhlash + 3 yangi bo'sh marshrut)   ← poydevor
     ↓
A2  Товары/Склад ajratish + StockMovement (BE-2) + Товары narx/hidden (BE-7)    ← katalog aniqlashadi
     ↓
A3  Касса ekrani + CashMovement ledger (BE-1)                                    ← naqd nazorati
     ↓
A4  Xarid lifecycle (BE-5) + Purchase Create sahifa + Suppliers master-detail    ← ta'minot zanjiri + seller "Поставки"
     ↓
A5  Возвраты ekrani + SaleReturn (BE-3)                                          ← qaytarish nazorati
     ↓
A6  Ruxsat modeli (preset+limit, BE-6) + Employees master-detail                 ← RBAC dizaynga keladi
     ↓
A7  Notification entity (BE-4) + feed serverga + header bell                      ← bildirishnomalar
     ↓
A8  Смены sverka + Посещаемость (BE-8) [force-close BE-10 ✅ tayyor — faqat UI tugma]  ← nazorat
     ↓
A9  Reports moslash + Dashboard moslash + Account (Telegram/sessiyalar, BE-9/11)  ← sayqal
```

Har bosqich mustaqil yetkaziladi. A1 birinchi — qolgan hammasi unga bog'lanadi. A2/A3 do'kon uchun eng qimmatli (katalog + naqd).

---

## 5. "Tayyor" mezoni (har bosqich uchun)

- `npx tsc -b --noEmit` va `npx eslint src` toza (0 warning).
- `dotnet build` + `dotnet test` o'tadi (backend tegilsa) + yangi entity uchun test.
- Yangi matnlar **uchala** locale'da (`ru.ts` manba, `uz.ts`/`en.ts` bir xil kalit; aks holda `tsc` sinadi).
- Yangi sahifa/amal ruxsat bilan himoyalangan (`perm(...)` marshrutda + `hasPermission` UI'da) + backend o'sha kalitni tekshiradi.
- Pul/qoldiqni qaytaradigan amallar (аннулировать, возврат, расход, инкассация, force-close) — **tasdiqlash oynasi** + **audit yozuvi** bilan.
- O'lchov/sana/summa — mavjud helperlar (`unitLabel`, `formatSum`, `formatQty`, Toshkent kuni) orqali; valyuta **сум**, ru-RU bo'shliqli mingliklar.
- Yangi ledger'lar (Cash/Stock) — mavjud amallarга (savdo, qarz to'lov, zakup, stocktake) **hook** bilan avtomatik yoziladi, qo'lda emas.
- **Atomiklik (majburiy):** yangi `CashMovement`/`StockMovement`/`SaleReturn` yozuvlari **o'sha operatsiyaning tranzaksiyasi ichida** yoziladi — loyihadagi mavjud pul-yaxlitligi andozasi (`IUnitOfWork.ExecuteInTransactionAsync` + `IAuditLogService.EnqueueActionAsync`, audit qatori bir xil commit ичида). Ledger yozuvi savdoni yaratib, keyin alohida chaqiruvda yozilmasin — yarim-yozilgan holat kassa/omborni buzadi.

---

## 6. Ochiq savollar (kelishilishi kerak)

1. **Ruxsat modeli (§2.13):** dizaynning preset+limit modeli backend granular kalitlari **ustiga** qatlam bo'ladimi, yoki kalitlar shu modelga qisqartiriladimi? (Tavsiya: qatlam — kalitlar saqlanadi, preset ular ustidan yoziladi.)
2. **Возвраты (§2.6):** mavjud `Sales/return-item` butunlay `SaleReturn`ga ko'chiriladimi yoki ikkalasi saqlanadimi?
3. **Клиент/Поставщик formalari:** dizayn minimal maydon ko'rsatadi (limit/kategoriya yo'q) — mavjud kengroq formalar **qisqartiriladimi** yoki dizayn soddalashtirilgan ko'rinishmi?
4. **Посещаемость (§2.15):** reja soatlari (08:00–20:00) va kechikish chegarasi (08:15) — `MarketSettings`ga sozlama sifatida chiqariladimi yoki qat'iy qoladimi?
5. **Audit/Settings:** dizayn to'plamiga kirmagan — alohida marshrut sifatida qoldiriladimi (backend tayyor) yoki yashiriladimi?
6. **Admin-kod override (§2.13):** dizayn «Изменение цены»/«Возвраты» o'chiq bo'lsa *«только с кодом администратора»* deydi — ruxsatsiz kassir admin-kod bilan bir martalik amal bajarishi. Bu tekis on/off dan farqli **override-kod** mexanizmi. Qilinadimi (kod so'raladigan modal + backend tekshiruvi + audit) yoki v1 uchun toggle o'chiq = amal butunlay bloklangan (kodsiz)?
