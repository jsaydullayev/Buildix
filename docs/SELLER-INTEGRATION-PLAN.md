# Seller (kassir) interfeysini integratsiya qilish — reja + backend gap-analizi

**Maqsad:** `docs/Web design seller` + `docs/Web design sellerPNG` dizaynidagi alohida **Seller (kassir)** interfeysini (9 sahifa, gorizontal top-nav shell) mavjud Buildix projectiga integratsiya qilish.

**Manba:** dizayn maketlari tahlili + 4 ta evidence-based backend/frontend gap-analiz agenti (barcha da'volar `file:line` bilan tekshirilgan, taxmin emas). Sana: 2026-07-22.

**Umumiy xulosa:** Frontend'ning ~70% i mavjud kod ustiga quriladi (api-qatlamlar, shared/ui, RBAC, i18n hammasi qayta ishlatiladi). Backend'ning katta qismi tayyor, lekin **2 ta butunlay yangi subsystem** (Поставки delivery-pipeline, In-app Notifications) va bir nechta o'rta gaplar (Микс split-to'lov, qarz-naqd→smena bog'lanishi, smena per-tender breakdown/history, Смена №) bor.

---

## 1. Dizayn xulosasi (nima quriladi)

Alohida kassir-ilova, **top-nav shell** (asosiy ilovaning sidebar'idan farqli). Dizayn tokenlari mos: primary `#2563eb`, navy `#0f2557` (top-bar sifatida), fon `#f6f8fb`, `Golos Text`, radius `13px` (modal `16px`).

**Shell (barcha sahifalarda bir xil):** chap nav (6) — Касса · Мои продажи · Товары · Клиенты · Долги · Поставки; o'ng — «Смена открыта · 08:00» pill (→Смены), 🔔 (→Уведомления), `46 чеков`, seller chip (→Аккаунт), logout.

**9 sahifa:** Касса(POS), Мои продажи, Смены, Товары, Клиенты, Долги, Поставки, Аккаунт, Уведомления.

**Kassir cheklovlari (dizaynda qat'iy):** cost/narх-закупа/наценка hech qayerda; mahsulot/postavka tahriri — admin; mijoz-limiti/o'chirish — admin; ma'lumot o'ziga tegishli («Мои» продажи/смены/результаты); til-almashtirish yo'q.

---

## 2. Frontend — reuse & build xaritasi

### 2.1 Yangi shell + routing
- **`app/layouts/SellerLayout.tsx`** (yangi) — `AppLayout` strukturasini nusxalash, `<Sidebar/>` o'rniga `<SellerTopNav/>`; `<RequireSubscription>` + `<Suspense><Outlet/></Suspense>` saqlanadi; `min-w-[1360px]` olib tashlanadi (responsive).
- **`app/layouts/SellerTopNav.tsx`** (yangi) — `Sidebar` pattern'i (NavLink + useParams + useTranslation + hasPermission), gorizontal; yangi `SELLER_NAV_ITEMS` (`shared/config/navigation.ts`).
- **`app/router.tsx`** — yangi route-guruh `/:subdomain/seller/*` (mavjud `/:subdomain/pos` bloki namunasida): `RequireAuth → SellerLayout`, bolalar `pos/sales/shifts/products/clients/debts/supplies/account/notifications`, har biri `perm(PERMISSIONS.x, <Page/>)`.
- **Role-based landing** — `shared/auth/useFirstAccessiblePath.ts`da `hasRole(ROLES.Seller)` bo'lsa `/:subdomain/seller/pos` qaytarish. LoginPage + IndexRedirect ikkalasi shu hook'ni ishlatgani uchun Seller login'dan keyin avtomatik seller-shell'ga tushadi; Owner/Admin sidebar-ilovada qoladi va seller-shell'ni qo'lda ko'ra oladi (hasPermission Owner/SuperAdmin uchun `true`).

### 2.2 9 sahifa — reuse jadvali
| # | Sahifa | Qayta ishlatiladigan kod | Tayyor API | Yangi ish |
|---|---|---|---|---|
| 1 | **Касса (POS)** | `features/pos/PosPage.tsx` + `pos/api.ts` (butun `posApi`) | createDraft, items add/remove, discount, customer, payments, mark-debt, product search, Customers | **Top-nav shell embed** (hozir standalone fullscreen); **held receipts** (hozir bitta `saleId`); **split/Микс to'lov** (hozir bitta method); **chek-preview modal** |
| 2 | **Мои продажи** | `features/sales/api.ts` (`listPaged`, `todaySummary`), SalesPage badge-pattern | `GET /Sales` (paged filtrlar), `GET /CashRegister/today-sales` | Seller-framed UI; o'z-savdolari (server `data.allSalesView`siz sellerга scope qiladi) |
| 3 | **Смены** | `features/shifts/api.ts`, `CloseShiftModal`, `WithdrawModal` | current/open/close, withdrawals, withdraw-request | Seller-framed (o'z smenasi + withdrawal **request**) |
| 4 | **Товары** | `features/warehouse/api.ts` (`productsApi.listPaged`, `categoriesApi.list`) | GetAllProductsPaged, GetAllCategories, GetUnits | Read-only browse UI (cost yashirin) |
| 5 | **Клиенты** | `pos/api.ts` ichidagi `searchCustomers/createCustomer`, `debts/api.ts` | Customers paged/create/byPhone, customer debts | **Yangi `features/customers/`** (pos'dan ajratish) + list/detail sahifa |
| 6 | **Долги** | `features/debts/api.ts`, `PayDebtModal` | debtors, summary, customerDebts, pay | Seller-framed (debtors + Принять оплату) |
| 7 | **Поставки** | `features/purchases/api.ts` | ReceiptsPaged, suppliers, reorder | Read-only + **приёмка** (backend gap — §3) |
| 8 | **Аккаунт** | `features/account/AccountPage.tsx` — **role-agnostik** | MyProfile, UpdateMyProfile, Sessions, LoginHistory, RevokeOtherSessions | Eng yuqori reuse — deyarli o'zgarishsiz |
| 9 | **Уведомления** | **Hech narsa** | yo'q | **Greenfield** — yangi `features/notifications/` + backend (§3) |

### 2.3 To'g'ridan-to'g'ri reuse (o'zgarishsiz)
`shared/ui` (Button, Card, Input, Badge, Spinner, PageHeader, StatCard, **Modal**, Toggle — lekin generic **Table yo'q**, tablitsalar per-feature Tailwind grid), `shared/api` (apiClient interceptorlar + PagedResult), `shared/auth` (useAuth `hasPermission/hasRole`, guards, useFirstAccessiblePath, sessionStore), `shared/config` (permissions, navigation, env), `shared/lib` (cn, format `formatSum/Qty/Date`), `shared/hooks/useDebounce`, dizayn tokenlari (`tailwind.config.ts`).

### 2.4 i18n
`ru.ts` = source of truth (`TranslationSchema = typeof ru`); `uz.ts`/`en.ts` bir xil kalitlarga ega bo'lishi shart (aks holda `tsc` sinadi). Yangi `seller:` seksiya qo'shiladi (nav, pos.heldReceipts/splitPayment, notifications...). Dizayn ruscha — `uz/en`ga ham shu kalitlar (avval ruscha qiymat bilan, keyin tarjima). Mavjud `pos.*/sales.*/shifts.*/debts.*/account.*` kalitlar qayta ishlatiladi.

---

## 3. Backend gap-analizi (evidence-based)

Belgilar: ✅ MAVJUD · ⚠️ QISMAN · ❌ YO'Q. Har biri `file:line` dalil bilan (agent-tekshiruvi).

### 3.1 POS / Savdolar
| # | Funksiya | Holat | Dalil / nima qurish |
|---|---|---|---|
| P1 | Draft = «Отложенные чеки» (yaratish/ro'yxat/resume/o'chirish) | ✅ | `CreateSaleAsync` Draft+SaleNumber (`SaleService.cs:41-107`); `GET /Sales/my-drafts`, `my-unfinished` (`SalesController.cs:104-131`). Kichik: seller o'z-draftini o'chirish `SalesDelete` ruxsatini talab qiladi (Sellerда yo'q) |
| P2 | Item add/remove/qty, mijoz biriktirish, chegirma | ✅ | items (`SalesController.cs:172-189`), customer (`:164-170`), **discount `SetSaleDiscount` (`:206-216`) — backendда bor, dizaynda yo'q**. Eslatma: absolute «set qty» yo'q (delta bilan) |
| P3 | Наличные / Карта | ✅ | `AddPayment` (`SalePaymentService.cs:77-318`), CARD→Terminal |
| P4 | **Микс (split: naqd+karta bitta checkoutда)** | ❌ | `AddPaymentDto` — bitta tender (`SaleDTOs.cs:99-111`). Ikki-chaqiruv walk-in (mijozsiz) uchun sinadi (`SalePaymentService.cs:150-154`) va atomik emas. **Qurish:** atomik multi-tender endpoint `POST /Sales/{id}/checkout {tenders:[...], dueDate?}` |
| P5 | В долг (oldindan to'lov + muddat) | ✅ | Bitta `AddPayment(type, partial, dueDate)` mijoz bilan → Debt+DueDate (`SalePaymentService.cs:159-278`). Faqat naqd+karta+qoldiq-qarz bir vaqtda yo'q (=P4) |
| P6 | Chek/print | ✅ | PDF `GET /Sales/{id}/invoice` (`:447-478`), data `GET /Sales/{id}` |
| P7 | Shtrix-kod / SKU aniq-qidiruv | ⚠️/❌ | `Product.Sku` bor-u **non-unique** (`Product.cs:13-15`), **Barcode maydoni yo'q**, by-sku endpoint yo'q. **Qurish:** `Product.Barcode` (indexed) + `GET /Products/by-barcode/{code}` |
| P8 | «Смена №» chekда + Sale↔Shift bog'lanishi | ❌ | `Sale`da `ShiftId` yo'q, `Shift`da ketma-ket raqam yo'q (`Shift.cs:15-16`). **Qurish:** `Shift.ShiftNumber` + `Sale.ShiftId` (yoki vaqt-oralig'i bilan resolve) + migratsiya |
| P9 | «Мои продажи» — o'z-savdolari **joriy smenaga** scope | ⚠️ | `GetSalesPaged` `sellerId/from/to` bor-u shift-filter yo'q (`SaleQueryService.cs:88-158`). **Qurish:** `GET /Sales/my-shift` yoki `shiftId` filtri |
| P10 | Joriy-smena stat (Продажи/Чеков/Средний/**Возвратов**) | ⚠️ | 3/4 `ShiftDto`da bor; **Возвратов hisoblanmaydi**. **Qurish:** `ReturnCount/ReturnAmount` `ComputeFinancialsAsync`ga |
| P11 | Qaytarish (Возврат) | ⚠️ | `ReturnSaleItem` bor (`SaleReversalService.cs:391-608`, negativ Payment yozadi) — lekin **returns-feed / negativ qatorlar yo'q**. **Qurish:** returns ro'yxati/proyeksiya |

### 3.2 Смена / Qarzlar
| # | Funksiya | Holat | Dalil / nima qurish |
|---|---|---|---|
| S1 | Smena open/close/current | ✅ | `ShiftsController.cs:29-59`, self-service (JWT) |
| S2 | Joriy-smena **per-tender breakdown** (Наличные/Карта/**В долг**/Средний + count'lar) | ⚠️ | `ComputeFinancialsAsync` (`ShiftService.cs:173-211`): Cash ✅, Card ✅ (lekin Terminal+Transfer+Click birga), **В долг yo'q**, per-tender count yo'q. **Qurish:** `DebtIn`, `ClickIn`, per-tender count'lar + AvgCheck |
| S3 | История смен (Неделя/Месяц/Всё + jadval + jami) | ⚠️/❌ | Per-shift agregatlar hisoblanadi (`GetUserShiftsAsync`), **lekin faqat `UsersShift` ruxsati ortida** (Sellerда yo'q), range-filtr yo'q, jami yo'q. **Qurish:** self-service `GET /Shifts/my?range=week\|month\|all` + totals |
| S4 | Close → owner report | ✅ | Telegram day-summary (`ShiftService.cs:116-128`) |
| S5 | Debtors ro'yxati (per-customer, чеков-в-долг, срок, долг) | ✅ | `GET /Debts/debtors` (`DebtQueryService.cs:76-133`) |
| S6 | Qarz-to'lov qabul (amount + method + partial/full) | ✅ | `POST /Debts/{id}/pay` (`DebtService.cs:34-186`), idempotent. Per-debt (per-customer emas) |
| S7 | **«оплата попадает в кассу текущей смены»** | ⚠️ | Kassa balansiga tushadi ✅ (`DebtService.cs:153`), **lekin yig'uvchi kassir smenasiga emas** — smena `Sale.SellerId` bo'yicha attribut qiladi, `Payment`da collector maydoni yo'q → cross-seller yig'ishda phantom nomuvofiqlik. **Qurish (pul-bug):** `Payment.CollectedByUserId`/`ShiftId` + `ComputeFinancials` collector bo'yicha |
| S8 | Stat: Всего/Просрочено/Принято сегодня | ✅ | `GET /Debts/summary` (`DebtQueryService.cs:135-166`, Tashkent kun). Kichik: PaidToday oldingi-to'lovlarni ham qo'shadi |
| S9 | «Принятые сегодня» ro'yxati | ❌ | Faqat sum bor, ro'yxat yo'q. **Qurish:** `GET /Debts/payments/today` |

### 3.3 Tovarlar / Mijozlar
| # | Funksiya | Holat | Dalil / nima qurish |
|---|---|---|---|
| T1 | Katalog: name/cat/unit/SKU/narх/qoldiq/status | ✅ | `ProductDto` (`ProductDTOs.cs`), low-stock `Product.cs:77`, `GET /Products/low-stock` |
| T2 | **МЕСТО (joy)** | ❌ | `Product`da location-maydoni yo'q. **Qurish:** `Product.WarehouseLocation` + migratsiya |
| T3 | **Штрих-код** | ❌ | =P7 |
| T4 | Detail: supplier/oxirgi-kelish/oyi-sotilgan | ❌ | Ma'lumot Zakup/SaleItemда bor-u agregatsiya/endpoint yo'q. **Qurish:** product-detail stats endpoint |
| T5 | Cost seller'dan yashirin | ✅ | `data.costPrice` RBAC (`ProductMapper.cs:19`) |
| C1 | Mijoz: name/phone/долг | ✅ | `CustomerDto.totalDebt` (`CustomerService.cs:83-95`) |
| C2 | **тип/постоянный** | ⚠️ | Saqlanadi (`Customer.cs:15,21`), yoziladi — **lekin read-mapperlar surface qilmaydi** (har GET `"Individual"` qaytaradi). **Tez tuzatish:** 3 mapperда DTO maydonlarini to'ldirish (migratsiyasiz) |
| C3 | Oyi-xaridlar (count+sum), oxirgi-xarid | ❌ | Hisoblanmaydi. **Qurish:** per-customer sales-stats endpoint |
| C4 | Mijoz qo'shish (name/phone/type) | ✅ | `POST /Customers/CreateCustomer` — `customers.manage` kerak (Sellerда yo'q) |

### 3.4 Postavka / Bildirishnomalar — 2 ta YANGI SUBSYSTEM
| # | Funksiya | Holat | Dalil / nima qurish |
|---|---|---|---|
| **PV1** | **Delivery pipeline** (в пути/ожидает/принята/задерживается + driver/ETA + «Начать приёмку») | ❌ **BUTUNLAY YO'Q** | `ZakupReceipt` = *tugallangan* priyomka; yaratilishi = stok qo'shilishi (`ZakupService.cs:199-200`). Faqat payment-status enum bor. Grep: inTransit/delivery/driver/eta/receiving = 0. **Qurish (yangi):** delivery lifecycle (status/driver/ETA/expected-qty), stok-ni yaratilishdan ajratish, receiving-action, seller-DTO shaping, migratsiya |
| PV2 | Seller summani ko'rmaydi | ✅ | `ZakupRoleShaper` (`ZakupRoleShaper.cs:14-19`) — yangi pipeline maydonlari ham seller-DTOга qo'shilishi shart |
| **N1** | **In-app Notifications** (feed, read/unread, deep-link, kun-guruh) | ❌ **BUTUNLAY YO'Q** | Notification entity/controller/endpoint yo'q; `notifications.access` — klient-gate (`PermissionKeys.cs:15-17`); TelegramNotifier faqat chiquvchi. **Qurish (yangi):** `Notification` entity + generatsiya-triggerlar (supply-arrived/debt-due/overdue/stock-out/shift-closed) + `GET /notifications`, `unread-count`, `read`, `read-all` + migratsiya |

### 3.5 Ruxsatlar (Seller role)
Seller default set (`PermissionDefaults.cs:41-52`): `DebtsAccess/Manage/DueDate` bor; **`UsersShift` yo'q** (shift-history), **`CashRegisterAccess/Manage` yo'q**, **`customers.manage` yo'q**, **`SalesDelete` yo'q** (o'z-draft), **`SalesInvoice`** — tekshirish kerak. **Qaror:** yo self-service endpoint (ruxsatsiz, JWT-scoped) qo'shish, yoki Seller role'ga kerakli ruxsatlarni berish.

---

## 4. Konsolidatsiyalangan gap ro'yxati (hajm bo'yicha)

**Katta (yangi subsystem/schema):**
1. **Поставки delivery pipeline** (PV1) — yangi feature (backend+frontend), migratsiya.
2. **In-app Notifications** (N1) — yangi feature (backend+frontend), migratsiya.
3. **Микс split-to'lov** (P4) — atomik multi-tender checkout endpoint.
4. **Qarz-naqd → yig'uvchi smena** (S7) — `Payment` schema + logika (pul-to'g'rilik).
5. **«Смена №» + Sale↔Shift** (P8) — schema + migratsiya.

**O'rta:**
6. Smena per-tender breakdown + Возвратов (S2, P10).
7. Seller shift-history endpoint (range+totals) (S3).
8. «Принятые сегодня» ro'yxati (S9).
9. Returns-feed / negativ qatorlar (P11).
10. «Мои продажи» joriy-smena scoping (P9).
11. Barcode maydoni + by-barcode lookup (P7/T3).
12. Product stats (joy/supplier/oxirgi-kelish/oyi-sotilgan) (T2, T4).
13. Customer stats (oyi-xaridlar/oxirgi) (C3).

**Kichik (tez):**
14. Customer тип/постоянный read-surface (C2) — migratsiyasiz.
15. Seller role ruxsat-grantlari (§3.5).
16. Seller o'z-draftini o'chirish ruxsati (P1).

**Frontend greenfield:** SellerLayout+TopNav+routing+role-landing, `features/customers`, `features/notifications`, held-receipts UI, split-payment UI, receipt-preview modal, seller-framed sahifalar.

---

## 5. Bosqichli yo'l-xarita (taklif)

- **Bosqich 0 — Shell (faqat frontend):** SellerLayout + SellerTopNav + SELLER_NAV_ITEMS + seller route-guruh + role-landing + i18n `seller:` seksiya. Natija: navigatsiya ishlaydi, Аккаунт (eng yuqori reuse) darhol.
- **Bosqich 1 — Reuse-og'ir sahifalar:** Товары (read-only), Долги, Мои продажи, Смены (asosiy), Клиенты (yangi feature). Backend: C2 quick-fix, P9/P10 kichik, ruxsat-grantlar.
- **Bosqich 2 — POS to'liq:** held-receipts (draft'lar tayyor), chek-preview (invoice PDF tayyor), checkout. Qaror: Микс split-to'lov (P4) — backend endpoint qurish yoki v1 uchun tashlab turish.
- **Bosqich 3 — Seller money-integrity:** S7 (qarz-naqd→smena, pul-bug), S2 per-tender breakdown, S3 shift-history + Смена № (P8), P11 returns-feed.
- **Bosqich 4 — Yangi subsystemlar:** Поставки delivery-pipeline (PV1), Notifications (N1) — har biri backend+frontend.
- **Bosqich 5 — Stats & polish:** product/customer stats (T2/T4/C3), barcode (P7), responsive/a11y.

Har bosqich: additiv (Flutter buzilmaydi), backend build + frontend tsc/lint/build yashil, money-kod ehtiyot bilan.

---

## 6. Ochiq qarorlar (boshlashdan oldin kelishish)
1. **Микс (split) to'lov** — atomik multi-tender endpoint quramizmi, yoki v1 uchun faqat Наличные/Карта/В долг (Микс keyinroq)?
2. **Chegirma (скидка)** — backendда bor; POS'ga chegirma boshqaruvi qo'shamizmi yoki dizayndek yashiramizmi?
3. **Поставки** — to'liq delivery-pipeline subsystem (katta) hozir quramizmi, yoki v1 uchun mavjud priyomka ustidan read-only?
4. **Notifications** — to'liq in-app subsystem hozir, yoki v1 uchun klient-tomon yig'ilgan feed (mavjud low-stock/debt endpointlaridan)?
5. **Seller ruxsatlari** — self-service endpointlar (ruxsatsiz) yoki Seller role'ga grant?
6. **Barcode** — yangi `Product.Barcode` maydoni yoki mavjud SKU'ni ishlatish?
7. **Ketma-ketlik** — yo'l-xarita bo'yicha bosqichma-bosqich yoki boshqa ustuvorlik?
