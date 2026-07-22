# Buildix — Code Review Report

**Sana:** 2026-07-21
**Review surface:** Buildix.Web (React 18 + TS SPA) frontend gap-fill va Buildix.API/.Application/.Domain/.Infrastructure (.NET 9) additive backend o'zgarishlari.
**Method:** Har bir da'vo Read/Grep/Glob bilan haqiqiy fayllarga solishtirilib tasdiqlangan (file:line keltirilgan). Faqat REAL defektlar, konkret failure-scenariy bilan.

---

## 1. Executive Summary

Jami **23 ta tasdiqlangan defekt** aniqlandi (2 ta bir xil topilma birlashtirildi).

| Severity | Soni |
|----------|------|
| Critical | 0 |
| **High** | **13** |
| **Medium** | **6** |
| **Low** | **4** |

Ustunlik bo'yicha ikkita katta klaster mavjud:

1. **Frontend↔backend route mismatch (7 ta High/Medium)** — `[Route("api/[controller]/[action]")]` templatega ega controllerlarda `[action]` tokeni URL yo'lida qoladi, lekin frontend uni tashlab yuborgan. Natijada butun-butun feature'lar (mahsulot edit/delete, qarz detali, xodim activate/deactivate, inventarizatsiya) 404 qaytaradi va umuman ishlamaydi.

2. **Debt / cash money-integrity (5 ta High)** — qarz limiti va "faqat doimiy mijozlarga qarz" qoidalari partial-payment yo'lida umuman tekshirilmaydi (moliyaviy nazorat bypass); smena rekonsiliatsiyasi konkurrent smenalarda va kechiktirilgan withdrawal approval'da noto'g'ri hisoblaydi.

**#1 eng muhim (birinchi tuzatilishi kerak):** `SalePaymentService.AddPaymentAsync` (SalePaymentService.cs:202) — har qanday oddiy sotuvchi partial-payment orqali qarz limiti va regulars-only qoidalarini jimgina chetlab o'tib, mijozga cheksiz qarz yozib qo'ya oladi. Bu bevosita moliyaviy/avtorizatsiya bypass.

---

## 2. Findings by Severity

### HIGH

---

#### H-1. `SalePaymentService.AddPaymentAsync` — qarz limiti va DebtOnlyForRegulars partial-payment yo'lida umuman tekshirilmaydi
**File:** `Buildix.Application/Services/SalePaymentService.cs:202`
**Dimension:** money-integrity / security-rbac (ikki topilma birlashtirildi)

**Nima noto'g'ri:** Qarz biznes-qoidalari (`MarketSettings.DebtOnlyForRegulars` va per-customer/market `DebtLimit`) FAQAT `SaleService.MarkSaleAsDebtAsync` da (SaleService.cs:224–250) amalga oshirilgan. Ammo ikkinchi, to'liq ochiq qarz-yaratish yo'li — `AddPaymentAsync` case 2 (SalePaymentService.cs:202–240) — partial payment bo'lganda (`0 < PaidAmount < TotalAmount`) `sale.Status = Debt` qilib yangi `Debt { RemainingDebt = TotalAmount − PaidAmount, Status = Open }` qatorini kiritadi, HECH QANDAY limit yoki regulars tekshiruvisiz. Service `IMarketSettingsService` ni inject ham qilmaydi (ctor 25–37), demak bu yo'lda gate umuman ishlay olmaydi. Endpoint `POST /api/Sales/{saleId}/payments` orqali ochiq, faqat `PermissionKeys.SalesCreate` (kassir darajasi) bilan himoyalangan.

**Failure scenario:** `DefaultDebtLimit = 15,000,000`, `DebtOnlyForRegulars = true`. Non-regular (yoki limitni to'ldirgan) mijozga 5,000,000 lik sotuv. Kassir "Долг" tugmasi o'rniga `/Sales/{id}/payments` ga 1,000 lik to'lov yuboradi → sotuv Debt ga o'tadi, ~4,999,000 Open debt yoziladi, ikkala moliyaviy qoida ham jimgina chetlab o'tiladi. Keyingi partial to'lovlar (update branch 232–238) ham tekshiruvsiz qarzni oshiraveradi.

**Fix:** `DebtOnlyForRegulars` + debt-limit tekshiruvini umumiy helper'ga chiqarib, `AddPaymentAsync` da Debt yaratish/yangilashdan oldin chaqiring (SaleService.cs:233–250 ni mirror qiling). `IMarketSettingsService` ni inject qiling, yoki barcha qarz-yaratishni bitta guarded metod orqali o'tkazing.

---

#### H-2. `productsApi.update` noto'g'ri route'ga PUT yuboradi
**File:** `Buildix.Web/src/features/warehouse/api.ts:97`
**Dimension:** api-contract

**Nima noto'g'ri:** `ProductsController` da `[Route("api/[controller]/[action]")]` (ProductsController.cs:17). `UpdateProduct` esa `[HttpPut("{id}")]` (line 123), shuning uchun haqiqiy route `/api/Products/UpdateProduct/{id}`. Frontend `PUT /api/Products/{id}` chaqiradi — hech qanday action bilan mos kelmaydi. (`create()` to'g'ri `/Products/CreateProduct` chaqiradi, kombinatsiya qoidasi shu bilan tasdiqlanadi.)

**Failure scenario:** Склад ekranidan istalgan mahsulotni tahrirlash `PUT /api/Products/{guid}` → 404. Mahsulot tahrirlari hech qachon saqlanmaydi.

**Fix:** `apiClient.put(\`/Products/UpdateProduct/${id}\`, { id, ...body })`.

---

#### H-3. `productsApi.remove` noto'g'ri route'ga DELETE yuboradi
**File:** `Buildix.Web/src/features/warehouse/api.ts:102`
**Dimension:** api-contract

**Nima noto'g'ri:** `DeleteProduct` `[HttpDelete("{id}")]` (ProductsController.cs:163), haqiqiy route `/api/Products/DeleteProduct/{id}`. Frontend `DELETE /api/Products/{id}` chaqiradi.

**Failure scenario:** Warehouse ro'yxatidan mahsulot o'chirish `DELETE /api/Products/{guid}` → 404; mahsulot o'chirilmaydi.

**Fix:** `apiClient.delete(\`/Products/DeleteProduct/${id}\`)`.

---

#### H-4. `productsApi.stocktake` noto'g'ri route'ga POST yuboradi
**File:** `Buildix.Web/src/features/warehouse/api.ts:117`
**Dimension:** api-contract
> Verify: empirik tekshiruvda ASP.NET Core route-jadvali `Stocktake -> api/Products/Stocktake/stocktake` deb chiqardi; severity Medium'dan **High**'ga ko'tarildi.

**Nima noto'g'ri:** `Stocktake` `[HttpPost("stocktake")]` (ProductsController.cs:152), controller templatega qo'shilib haqiqiy route `/api/Products/Stocktake/stocktake` bo'ladi. Frontend `/api/Products/stocktake` ga POST qiladi — hech nima bilan mos kelmaydi.

**Failure scenario:** Инвентаризация (bulk stock count) `POST /api/Products/stocktake` → 404; stocktake hech qachon qo'llanmaydi.

**Fix:** `apiClient.post('/Products/Stocktake/stocktake', { items })`.

---

#### H-5. `debtsApi.customerDebts` noto'g'ri route'ga GET yuboradi
**File:** `Buildix.Web/src/features/debts/api.ts:48`
**Dimension:** api-contract

**Nima noto'g'ri:** `DebtsController` da `[Route("api/[controller]/[action]")]` (line 14). Ko'pchilik endpoint absolute `~/api/Debts/...` route bilan qochgan, lekin `GetCustomerDebts` `[HttpGet("{customerId}")]` (line 57) `~/` prefiksisiz, shuning uchun haqiqiy route `/api/Debts/GetCustomerDebts/{customerId}`. Frontend `GET /api/Debts/{guid}` chaqiradi.

**Failure scenario:** Mijozning qarz detalini ochish `GET /api/Debts/{guid}` → 404; mijoz qarz ro'yxati hech qachon yuklanmaydi.

**Fix:** `apiClient.get(\`/Debts/GetCustomerDebts/${customerId}\`)`, yoki backend action'ga `[HttpGet("~/api/Debts/customer/{customerId}")]` absolute route qo'shing.

---

#### H-6. `employeesApi.activate` noto'g'ri route'ga POST yuboradi
**File:** `Buildix.Web/src/features/employees/api.ts:49`
**Dimension:** api-contract

**Nima noto'g'ri:** `ActivateUser` `[HttpPost("{id}/activate")]` (UsersController.cs:211), controller `[action]` templati bilan haqiqiy route `/api/Users/ActivateUser/{id}/activate`. Frontend `/api/Users/{id}/activate` ga POST qiladi.

**Failure scenario:** Xodimni Activate qilish `POST /api/Users/{guid}/activate` → 404; xodim qayta faollashtirilmaydi.

**Fix:** `apiClient.post(\`/Users/ActivateUser/${id}/activate\`)`.

---

#### H-7. `employeesApi.deactivate` noto'g'ri route'ga POST yuboradi
**File:** `Buildix.Web/src/features/employees/api.ts:52`
**Dimension:** api-contract

**Nima noto'g'ri:** `DeactivateUser` `[HttpPost("{id}/deactivate")]` (UsersController.cs:200), haqiqiy route `/api/Users/DeactivateUser/{id}/deactivate`. Frontend `/api/Users/{id}/deactivate` ga POST qiladi.

**Failure scenario:** Xodimni Deactivate qilish `POST /api/Users/{guid}/deactivate` → 404; xodim deaktivatsiya qilinmaydi.

**Fix:** `apiClient.post(\`/Users/DeactivateUser/${id}/deactivate\`)`.

> **Eslatma (H-2…H-7):** Bu route-mismatch klasteri bir xil ildizga ega — `[action]` tokeni. Bir marta audit qilib, `api.ts` fayllarini backend route'lariga to'liq moslashtirib chiqish tavsiya etiladi (birgina grep bilan barcha `apiClient.(get|put|post|delete)` chaqiruvlarini controller action nomlariga solishtiring).

---

#### H-8. Telegram webhook autentikatsiyasi yo'q — market notification kanalini o'g'irlash mumkin
**File:** `Buildix.API/Controllers/TelegramController.cs:28`
**Dimension:** security-rbac

**Nima noto'g'ri:** Webhook `[AllowAnonymous]` (line 29) va request body'ga ko'r-ko'rona ishonadi: `message.from.username` va `message.chat.id` ni o'qib `_notifier.TryLinkChatAsync(username, chatId)` chaqiradi (44–49). `TelegramNotifier.TryLinkChatAsync` (TelegramNotifier.cs:64–76) `OwnerTelegram` handle case-insensitive mos kelgan BIRINCHI `MarketSettings` ni topib `OwnerTelegramChatId` ni attacker bergan `chatId` ga yozib qo'yadi. Repo-da `X-Telegram-Bot-Api-Secret-Token` / secret_token tekshiruvi umuman yo'q (grep 0 natija), path ham qat'iy `/api/telegram/webhook`.

**Failure scenario:** Attacker jabrlanuvchi eganing ochiq Telegram handle'sini bilib (`OwnerTelegram` Настройки da saqlanadi) forged body POST qiladi: `{"message":{"chat":{"id":<ATTACKER>},"from":{"username":"<victimHandle>"}}}`. Shundan keyin har bir `SendToOwnerAsync` (cash-withdrawal so'rovlari — summa + so'rovchi ismi CashRegisterService.cs:272–274; smena yopilishi ShiftService.cs:127) attacker'ga boradi, haqiqiy egaga esa bildirishnomalar to'xtaydi.

**Fix:** `setWebhook` da `secret_token` o'rnatib, har so'rovda `X-Telegram-Bot-Api-Secret-Token` header'ni tekshiring (mos kelmasa reject). Qo'shimcha: bir martalik link-nonce, chat faqat linkni boshlagan account'ga bog'lanishi uchun. Faqat attacker bergan `username` bo'yicha chat link qilmang.

---

#### H-9. Auth guard cheksiz redirect / to'liq lockout — `dashboard.access` olib tashlangan har qanday user uchun
**File:** `Buildix.Web/src/shared/auth/guards.tsx:38`
**Dimension:** frontend-correctness

**Nima noto'g'ri:** `RequirePermission` rad etilgan userni `/${subdomain}/dashboard` ga yo'naltiradi (guards.tsx:38). Ammo `dashboard` route'ining o'zi aynan shu permission bilan himoyalangan (router.tsx:55 `perm(PERMISSIONS.dashboard.access, ...)`), index route `dashboard` ga redirect qiladi (router.tsx:54), va LoginPage login'dan keyin `/${subdomain}/dashboard` ga navigate qiladi (LoginPage.tsx:43). `dashboard.access` — Owner matritsasida oddiy grantable/revocable kalit (faqat `DataCostPrice`/`DataProfit` hard-forbidden; PermissionDefaults.cs:61–65). `UpdateUserPermissionsAsync` (UserService.cs:574–584) bu kalitni saqlash bo'yicha hech qanday guard qo'ymaydi.

**Failure scenario:** Owner Seller uchun `dashboard.access` ni oladi. Seller login qiladi → `/acme/dashboard` → guard fail → `Navigate` `/acme/dashboard` → yana fail → cheksiz loop ("Maximum update depth exceeded" / muzlagan bo'sh ekran). Login ham `/dashboard` ga tushgani uchun Seller ruxsati bor Sales/POS ga ham hech qachon yeta olmaydi — to'liq lockout.

**Fix:** Permission fail'ni permission-gated bo'lmagan route'ga yo'naltiring (masalan static "no access" ekran yoki `/account`), yoki user ruxsati bor birinchi nav-item'ga. Minimal: agar rad etilgan path == redirect target bo'lsa, `Navigate` o'rniga fallback render qiling.

---

#### H-10. Smena rekonsiliatsiyasi tasdiqlangan withdrawal'ni so'rov vaqti bo'yicha hisoblaydi, pul chiqqan vaqt (approval) bo'yicha emas
**File:** `Buildix.Application/Services/ShiftService.cs:187`
**Dimension:** money-integrity
> Verify: severity **High** ga ko'tarildi (soxta discrepancy → noto'g'ri kassir aybdor qilinadi).

**Nima noto'g'ri:** `ComputeFinancialsAsync` withdrawal'larni smena oynasiga `w.WithdrawalDate` bo'yicha kiritadi (187–191). Ammo owner-approval yo'lida `WithdrawalDate` REQUEST vaqtida qo'yiladi (`RequestWithdrawalAsync`, CashRegisterService.cs:252), pul esa faqat APPROVAL vaqtida debit qilinadi (`ApproveWithdrawalAsync`, CashRegisterService.cs:300). `ApproveWithdrawalAsync` `WithdrawalDate` ni yangilamaydi. Mo'ljallangan `CashWithdrawal.ShiftId` maydoni (CashWithdrawal.cs:27 "rekonsiliatsiya uchun") hech qayerda to'ldirilmaydi (grep 0 natija) — attribution ulanmagan.

**Failure scenario:** Smena A (Du 10:00) da 500,000 so'rov (Pending, A dan chiqarilmagan — to'g'ri). Smena A yopiladi. Smena B (Se) da owner 11:00 da tasdiqlaydi — register 500,000 ga debit, pul Seshanba kassasidan olinadi. Lekin `WithdrawalDate` (Du 10:00) < B.OpenedAt, shuning uchun B dan ayirilmaydi → B kassasi 500,000 kam chiqadi → ReconStatus = Discrepancy, Seshanba kassiri soxta kamomad uchun ayblanadi.

**Fix:** Approval yo'li uchun effektiv cash-out timestamp bo'yicha oynalang (`ApprovedAt` agar `Approved`, aks holda `WithdrawalDate`), yoki approval vaqtida `CashWithdrawal.ShiftId` ni to'ldirib shu bo'yicha filtrlang.

---

#### H-11. Smena moliyasi market-bo'ylab barcha to'lov/sotuvlarni sanaydi, o'z kassirini emas — konkurrent smenalarda ikki barobar hisoblaydi
**File:** `Buildix.Application/Services/ShiftService.cs:178`
**Dimension:** money-integrity
> Verify: severity **High** ga ko'tarildi.

**Nima noto'g'ri:** `OpenShiftAsync`/`FindOpenShiftAsync` faqat SHU user uchun ikkinchi ochiq smenani bloklaydi (208 UserId bo'yicha), shuning uchun bir nechta kassir bitta per-market `CashRegister` ustida bir vaqtda ochiq smenaga ega bo'la oladi. `ComputeFinancialsAsync` esa cashIn/cardIn (178–185), revenue/checkCount (193–197) ni FAQAT MarketId + vaqt oynasi bilan agregatlaydi — hech qachon smena sotuvchisi bo'yicha emas. `OpeningCash` ham oxirgi CLOSED smenadan olinadi (59–63), shuning uchun ikki konkurrent smena bir xil baseline'ni qayta ishlatadi.

**Failure scenario:** Kassir A va B ikkalasi 09:00 da bitta register'da smena ochadi. 09:00–12:00 orasida A 800,000 va B 600,000 naqd sotadi. Har ikkala smenani ko'rish/yopishda `ComputeFinancialsAsync` = 1,400,000. Har birining expected cash = OpeningCash + 1,400,000 − withdrawals; har bir smena boshqasining sotuvini ham Revenue/CheckCount ga qo'shadi. Kassani bir marta sanash ~600k–800k phantom kamomad beradi; per-shift market totallar ~2x shishadi.

**Fix:** Yo per-market (register) bitta ochiq smenani majburlang, yoki cashIn/cardIn/revenue/checkCount ni smena sotuvchisi bo'yicha filtrlang (`p.Sale.SellerId == s.UserId`, `x.SellerId == s.UserId`). Per-seller filtr allaqachon SaleQueryService.cs:105/191/209, DashboardService.cs:360, SalesReportService.cs:228 da qo'llanilgan pattern.

---

#### H-12. (PLAUSIBLE) Noto'g'ri joriy parol (401) token-refresh interceptor bilan to'qnashadi va userni logout qilishi mumkin
**File:** `Buildix.Web/src/shared/api/client.ts:63`
**Dimension:** critic

**Nima noto'g'ri:** `UsersController.UpdateMyProfile` noto'g'ri joriy parolda `UnauthorizedAccessException` (UserService.cs:277) ni tutib `Unauthorized(ex.Message)` — HTTP 401 qaytaradi. Axios response interceptor `/Auth/Login` va `/Auth/RefreshToken` bo'lmagan HAR QANDAY 401 ni access-token muddati tugagan deb hisoblaydi: `refreshAccessToken()` ni ishga tushiradi va so'rovni qayta yuboradi. Sessiya haqiqatda valid bo'lgani uchun refresh muvaffaqiyat bo'ladi, retry yana 401 qaytaradi — har bir noto'g'ri parol urinishi refresh round-trip sarflaydi va refresh token'ni rotate qiladi. Agar `refreshAccessToken()` null qaytarsa (masalan token konkurrent rotate bo'lgan), interceptor `sessionApi.clear()` (client.ts:73) — user shunchaki parolni xato terganidan logout bo'ladi.

**Failure scenario:** Account sahifasida user parol o'zgartiradi va joriy parolni xato teradi → 401 → keraksiz token refresh (rotation) + retry → to'g'ri "current password is incorrect" xatosi kechikadi. Refresh fail bo'lsa — force logout.

**Fix:** Noto'g'ri joriy parol uchun 400 (BadRequest) qaytaring, yoki interceptor'da business-level 401 larni istisno qiling (faqat token-expired kod/header bo'lganda refresh qiling). 401 ni faqat haqiqiy auth/token nosozliklariga saqlang.

---

#### H-13. (PLAUSIBLE) Dashboard "recent sales" va reports category-sales Tashkent day-range builder'ga UTC instant yuboradi — non-Tashkent serverda noto'g'ri kun
**File:** `Buildix.Web/src/features/dashboard/DashboardPage.tsx:53`
**Dimension:** critic

**Nima noto'g'ri:** DashboardPage `today = startOfDay(new Date()).toISOString()` (line 53) qurib `GET /Reports/daily-sales-list?date=...` chaqiradi. `reports/api.ts` `categorySales(range.end)` ham `now.toISOString()` yuboradi. Backend `SalesListService.GetDailySalesListAsync` → `TashkentClock.LocalDayToUtcRange` `localDate.Date` qiladi (vaqtni tashlab, DATE komponentini Tashkent kalendar yarim tuni deb oladi). Ammo `.toISOString()` Tashkent yarim tunini allaqachon 5 soat oldingi UTC kuniga surib yuboradi (Tashkent 2026-07-21 00:00 → `2026-07-20T19:00:00Z`). UTC serverda `.Date` = 2026-07-20 — service Tashkent 07-20 kunini qaytaradi.

**Failure scenario:** Production API UTC da. Kassir Tashkent 10:00, 2026-07-21 da dashboard ochadi. "Recent sales" widget va kunlik totallar 2026-07-20 (kecha) sotuvlarini ko'rsatadi; bugungi tranzaksiyalar kun bo'yi yo'qoladi. Reports "Sales by category" ham xuddi shu off-by-one.

**Fix:** Day-range quruvchi endpoint'larga toza Tashkent kalendar sanasini yuboring (`format(new Date(),'yyyy-MM-dd')`), YOKI server tarafda kelayotgan instant'ni `.Date` olishdan oldin Tashkent-local'ga o'giring. Instant-based (period) report'lar o'z holicha qolsin.

---

### MEDIUM

---

#### M-1. `productsApi.units` noto'g'ri route'ga GET yuboradi — unit dropdown bo'sh qoladi
**File:** `Buildix.Web/src/features/warehouse/api.ts:112`
**Dimension:** api-contract

**Nima noto'g'ri:** `GetUnits` `[HttpGet("units")]` (ProductsController.cs:102), haqiqiy route `/api/Products/GetUnits/units`. Frontend `/api/Products/units` chaqiradi → 404. `ProductFormModal.tsx:205` `unitsQuery.data ?? []` ni render qiladi, shuning uchun dropdown bo'sh.

**Failure scenario:** Mahsulot create/edit formasidagi unit dropdown (dona/kg/m) `GET /api/Products/units` → 404, ro'yxat yuklanmaydi. (Forma `unit:1` default o'rnatgani uchun create to'liq bloklanmaydi — shuning uchun Medium.)

**Fix:** `apiClient.get('/Products/GetUnits/units')`.

---

#### M-2. `GetDebtorsAsync` qarzdorlarni takrorlaydi va bitta qarz summasini mijoz jami o'rniga ko'rsatadi
**File:** `Buildix.Application/Services/SaleQueryService.cs:218`
**Dimension:** backend-correctness

**Nima noto'g'ri:** `GetDebtorsAsync` (GET `/api/sales/debtors`, SalesController.cs:342) har bir ochiq `Debt` qatoriga bitta `CustomerDto` proyeksiya qiladi, `d.RemainingDebt` (bitta qarz) ni mijoz qarzi sifatida ishlatib `.Distinct()` chaqiradi. `CustomerDto` — value-equality bo'lgan record; ikki qarz `RemainingDebt` bilan farq qilgani uchun `Distinct` ularni birlashtirmaydi. `GroupBy(CustomerId)/Sum` yo'q (yangi `DebtQueryService.GetDebtorSummariesAsync` to'g'ri qiladi).

**Failure scenario:** Customer A da 3 ta ochiq qarz (100k, 200k, 300k). `/api/sales/debtors` A ni 3 marta qaytaradi, har biri 100k/200k/300k ko'rsatadi, 600k bilan bir marta emas. Totallar va qarzdor ro'yxati noto'g'ri.

**Fix:** Ochiq qarzlarni `CustomerId` bo'yicha guruhlab per-customer `RemainingDebt` yig'ing (`DebtQueryService.GetDebtorSummariesAsync` ni mirror qiling), har qarzdorga bitta `CustomerDto`.

---

#### M-3. Cash register "recent withdrawals" ro'yxati Pending/Rejected so'rovlarni pul chiqqandek ko'rsatadi
**File:** `Buildix.Application/Services/CashRegisterService.cs:87`
**Dimension:** backend-correctness

**Nima noto'g'ri:** `GetCashRegisterAsync` `CashWithdrawals` ni FAQAT `MarketId` bo'yicha filtrlaydi (87–92), `ApprovalStatus` filtri yo'q; `CashWithdrawalDto` da status maydoni yo'q. `RequestWithdrawalAsync` `Pending` qator kiritadi (balansdan ayirmasdan); `RejectWithdrawalAsync` qatorni `Rejected` qoldiradi (u ham ayrilmagan). Ikkalasi ham ro'yxatga tushadi, haqiqiy tugatilgan withdrawal'dan farqlanmaydi. `ShiftService.ComputeFinancialsAsync` (line 190) to'g'ri faqat `NotRequired || Approved` ni oladi — ikki ko'rinish kelishmaydi.

**Failure scenario:** Admin 500k withdrawal so'rovi (Pending) yuboradi (`CashWithdrawalNeedsApproval=true`). Owner cash register ekranini ochadi: `CurrentBalance` o'zgarmagan (to'g'ri) lekin ro'yxatda hali bo'lmagan 500k withdrawal ko'rinadi. Rad etilsa ham ro'yxatda qoladi. `DashboardPage.tsx:98–102` buni "withdrawals today" hint'iga qo'shadi.

**Fix:** Ro'yxatni faqat tugatilgan harakatlarga filtrlang: `Where(x => x.MarketId == marketId && (x.ApprovalStatus == NotRequired || x.ApprovalStatus == Approved))`, yoki DTO ga `approvalStatus` qo'shib UI da farqlang.

---

#### M-4. POS quantity "+" tugmasi mahsulot joriy qidiruv natijasida bo'lmasa jimgina hech narsa qilmaydi
**File:** `Buildix.Web/src/features/pos/PosPage.tsx:270`
**Dimension:** frontend-correctness

**Nima noto'g'ri:** Cart-line increment handler mahsulotni jonli qidiruv natijasidan qidiradi: `productsQuery.data?.items.find((x) => x.id === it.productId); if (p) addItem.mutate(p)` (270–273). `productsQuery` joriy debounced qidiruv bilan keyed va faqat ~30 mos mahsulotni ushlaydi. Qidiruv o'zgarsa/tozalansa, mahsulot ro'yxatda yo'q → `find` undefined → click no-op, feedback yo'q. Decrement (`removeOne.mutate(it.id)`, line 261) itemId bilan ishlaydi — asimmetrik.

**Failure scenario:** Kassir "cement" qidirib Cement qo'shadi, keyin "sand" qidiradi. Cement liniyasidagi "+" ni bosadi — hech narsa bo'lmaydi, quantity 1 da qoladi. Oshirish uchun "cement" ni qayta qidirishi kerak. (>30 mahsulotda default ro'yxat ham qo'shilgan mahsulotni tashlab qoldirishi mumkin.)

**Fix:** Increment'ni sale item id bo'yicha alohida endpoint/mutation bilan qiling (removeItem kabi), yoki cart-line'dagi mavjud data (`it.productId`/`it.salePrice`) bilan qo'shing.

---

#### M-5. POS product-grid stock soni add/checkout'dan keyin yangilanmaydi (`['pos-products']` invalidatsiya yo'q)
**File:** `Buildix.Web/src/features/pos/PosPage.tsx:61`
**Dimension:** frontend-correctness

**Nima noto'g'ri:** `['pos-products', debouncedSearch]` query (61–65) hech qachon invalidate qilinmaydi. `addItem` (79–84) va `checkout` (107–117) faqat `['pos-sale']` ni invalidate qiladi; `refetchOnWindowFocus` global o'chirilgan (queryClient.ts:8); query bir xil key ostida mounted qoladi. Natijada ko'rsatilgan stock va `disabled={p.quantity <= 0}` guard (line 188) butun POS sessiya davomida oxirgi qidiruv paytidagi stock'ni aks ettiradi.

**Failure scenario:** Mahsulot stock'da 3 ko'rsatadi. Kassir 3 tasini sotadi. Grid hali "3" ko'rsatadi va enabled qoladi. Keyingi mijozda kassir yana bosadi — add backend'da rad etiladi (yoki server yumshoq bo'lsa oversell), ekrandagi stock kun bo'yi noto'g'ri. (Backend stock'ni majburlagani uchun Medium — real oversell yo'q, faqat stale UI.)

**Fix:** `addItem`, `removeItem`, `checkout` ning `onSuccess` da `['pos-products']` ni invalidate qiling (yoki qisqa `staleTime` + har cart-mutation'da refetch).

---

#### M-6. (PLAUSIBLE) 404 javoblarda body yo'q — frontend har not-found holatida bo'sh / "Network error" ko'rsatadi
**File:** `Buildix.API/Controllers/ApiControllerBase.cs:49`
**Dimension:** critic

**Nima noto'g'ri:** `ApiControllerBase.ToActionResult` `Result.Code == "NOT_FOUND"` ni bodysiz `NotFound()` ga map qiladi (48–49 va 60–61). Ko'p servicelar mazmunli xabar bilan `NOT_FOUND` qaytaradi (masalan `SaleService` "Sale not found", `CashRegisterService` "Запрос не найден"), lekin `NotFound()` JSON yozmagani uchun xabar yo'qoladi. Frontend `normalizeError` `{ message }` shaklini kutadi; bodysiz 404 da `error.response.data` bo'sh string, `typeof data === 'string'` branch message ni `''` qiladi (yoki "Network error" default qoldiradi).

**Failure scenario:** POS user boshqa tab'da o'chirilgan sale'ni ochadi → `posApi.getSale` 404 bodysiz → UI "Sale not found" o'rniga bo'sh yoki "Network error" ko'rsatadi.

**Fix:** `NotFound(new { message = result.Error })` (va ixtiyoriy code) qaytaring, 404 ham 400 kabi `{ message }` envelope tashisin.

---

### LOW

---

#### L-1. Debt summary "paid today"/"paid this month" UTC kun/oy chegaralarini ishlatadi, Tashkent biznes-kunini emas
**File:** `Buildix.Application/Services/DebtQueryService.cs:137`
**Dimension:** backend-correctness

**Nima noto'g'ri:** `GetSummaryStatsAsync` `todayStart = DateTime.UtcNow.Date` va `monthStart` ni UTC dan hisoblaydi, keyin `p.CreatedAt >= todayStart/monthStart` bilan yig'adi — UTC yarim tun chegaralari. Boshqa joylar biznes-kunni Tashkent (UTC+5) da aniqlaydi (`ITashkentClock.LocalDayToUtcRange` — CashRegisterService.cs:430, SalesController.cs:96, TashkentClock.cs:20). 00:00–05:00 Tashkent to'lovlari oldingi UTC kuniga tushadi.

**Failure scenario:** 02:00 Tashkent (21:00 UTC oldingi kun) qarz to'lovi "paid today" dan chiqib oldingi kunga hisoblanadi; egaga ko'rsatilgan "paid today" har kuni erta-tong oynasida kam ko'rsatiladi.

**Fix:** `todayStart`/`monthStart` ni `_clock` orqali oling (`LocalDayToUtcRange` / `TodayLocal`), boshqa servicelar kabi.

---

#### L-2. (PLAUSIBLE) `RequestWithdrawalAsync` `request.WithdrawType` ni e'tiborsiz qoldirib doim cash yozadi
**File:** `Buildix.Application/Services/CashRegisterService.cs:255`
**Dimension:** backend-correctness

**Nima noto'g'ri:** `RequestWithdrawalAsync` pending `CashWithdrawal` ga `WithdrawType = WithdrawTypeCash` ni hardcode qiladi, `request.WithdrawType` ni e'tiborsiz qoldiradi. `ApproveWithdrawalAsync` `WithdrawType == 'cash'` bo'lgan har qatorni balansdan ayiradi. `WithdrawCashAsync` (immediate yo'l) esa `request.WithdrawType` ni hurmat qiladi — ikki yo'l nomuvofiq. Ammo frontend request chaqiruvi type'ni hardcode qiladi (`shifts/api.ts:43` `withdrawType:'cash'`), shuning uchun in-app zararli scenariy hozircha yetib bo'lmaydi (PLAUSIBLE).

**Failure scenario:** `CashWithdrawalNeedsApproval=true` marketda qo'lda yasalgan API so'rovi `withdrawType:'click'` yuborsa, u cash sifatida saqlanadi; approve'dan keyin `CurrentBalance` 1,000,000 ga kamayadi, kassa shu miqdorga kam chiqadi.

**Fix:** `RequestWithdrawalAsync` da `WithdrawType` ni `request.WithdrawType` dan oling (cash/click validatsiya bilan), `WithdrawCashAsync` ga moslang.

---

#### L-3. Sessions ro'yxati noto'g'ri sessiyani "current" deb belgilaydi (isCurrent ishonchsiz)
**File:** `Buildix.API/Controllers/AuthController.cs:112`
**Dimension:** security-rbac

**Nima noto'g'ri:** Sessions endpoint `GetSessionsAsync(userId, null, ct)` — doim `null` `currentRefreshToken` uzatadi. `AuthService.GetSessionsAsync` (196–199) da null token `currentId` ni `active[0].Id` ga tushiradi, ya'ni `OrderByDescending(LastUsedAt ?? CreatedAt)` (line 188) dan keyingi eng oxirgi refresh qilgan qurilma — so'rov qilayotgan qurilma emas. Cross-user leak yo'q (userId bo'yicha scoped). `RevokeOtherSessions` client token'ni to'g'ri ishlatadi — ta'sirlanmaydi.

**Failure scenario:** User telefonda (10:00 refresh) va desktopda (10:30 refresh) kirgan, telefondan "Устройства и сессии" ochadi. Desktop sessiyasi `isCurrent=true` deb belgilanadi (yaqinroq ishlatilgan), user noto'g'ri qurilmani "bu qurilma" deb ko'radi — rogue sessiyani noto'g'ri baholashi mumkin.

**Fix:** `GetSessionsAsync` ga caller'ning haqiqiy refresh token'ini (yoki sessiya identifikatorini) uzating, `isCurrent` aniq token-match bilan hisoblansin.

---

#### L-4. Employees sahifasi shartsiz `/Reports/staff-performance` chaqiradi — `reports.access` yo'q userga 403
**File:** `Buildix.Web/src/features/employees/EmployeesPage.tsx:41`
**Dimension:** frontend-correctness

**Nima noto'g'ri:** `perfQuery` (`useQuery({ queryKey: ['staff-perf'], queryFn: employeesApi.staffPerformance })`, line 41) har tashrifda `enabled` permission gate'siz ishlaydi. U `/Reports/staff-performance` (api.ts:37) ga uradi, backend `[RequirePermission(PermissionKeys.ReportsAccess)]` (ReportsController.cs:144–145) bilan himoyalangan. Employees route faqat `users.access` bilan gated (router.tsx:62). `users.access` bor lekin `reports.access` yo'q user har render'da kafolatlangan 403 oladi. 403 yutiladi (interceptor faqat 401/402/423 ni special-case qiladi), perf tiles 0 ga tushadi.

**Failure scenario:** Admin `users.access` bor, `reports.access` yo'q. Employees sahifasi `GET /api/Reports/staff-performance` → har safar 403; har xodim kartasi 0 chek / 0 daromad / 0 smena ko'rsatadi.

**Fix:** `perfQuery` ga `enabled: hasPermission(PERMISSIONS.reports.access)` qo'shing va caller'da reports access bo'lmasa per-employee statistikani yashiring.

---

## 3. Areas Reviewed / Looked Clean

- **Multi-tenant scoping** — tekshirilgan money/query servicelar (`SaleQueryService`, `SalesListService`, `DashboardService`, `DebtQueryService`) `MarketId` bilan to'g'ri scope qiladi; cross-market data leak topilmadi (yagona istisno H-8 Telegram linking, u alohida attack-surface).
- **`RevokeOtherSessionsAsync`** (AuthService.cs:215) — client-supplied refresh token'ni to'g'ri hash qilib ishlatadi; L-3 defekti unga taalluqli emas.
- **`WithdrawCashAsync`** (immediate withdrawal path) — `request.WithdrawType` ni hurmat qiladi va balansni faqat cash uchun to'g'ri debit qiladi.
- **Debt limit / regulars enforcement** `MarkSaleAsDebtAsync` (SaleService.cs:224–250) da to'g'ri implementatsiya qilingan — muammo faqat parallel `AddPaymentAsync` yo'lida (H-1).
- **`create()` / `CreateProduct`, `CreateUser`, `GetAllUsers`** kabi frontend chaqiruvlari action-token'ni to'g'ri saqlaydi — route-mismatch faqat sanab o'tilgan endpointlarda.
- **Per-seller filtering pattern** (`s.SellerId == userId`) reports/query servicelarida izchil qo'llanilgan — yagona chetga chiqish `ComputeFinancialsAsync` (H-11).
- **`ShiftService.ComputeFinancialsAsync` withdrawal status filtri** (`NotRequired || Approved`, line 190) — to'g'ri; buzuqligi vaqt-attribution'da (H-10), status'da emas.

---

## 4. Prioritized Fix Backlog

| # | Priority | Finding | File:line | Effort |
|---|----------|---------|-----------|--------|
| 1 | P0 | H-1 Debt-limit/regulars bypass (partial payment) | SalePaymentService.cs:202 | O'rta (helper + inject) |
| 2 | P0 | H-8 Telegram webhook auth yo'q (kanal hijack) | TelegramController.cs:28 | O'rta (secret-token) |
| 3 | P0 | H-2…H-7 Route mismatch klasteri (6 endpoint 404) | warehouse/debts/employees `api.ts` | Kichik (URL fix ×6) |
| 4 | P0 | H-9 Auth-guard infinite redirect / lockout | guards.tsx:38 + router.tsx:55 | Kichik (fallback route) |
| 5 | P1 | H-11 Konkurrent smena double-count | ShiftService.cs:178 | O'rta (per-seller filtr) |
| 6 | P1 | H-10 Withdrawal shift-attribution (approval vaqti) | ShiftService.cs:187 | O'rta |
| 7 | P1 | H-13 Dashboard UTC→Tashkent day off-by-one | DashboardPage.tsx:53 | Kichik (yyyy-MM-dd) |
| 8 | P1 | H-12 Wrong-password 401 ↔ refresh interceptor | client.ts:63 / UsersController | Kichik (400 qaytar) |
| 9 | P2 | M-2 Debtors duplication/summa | SaleQueryService.cs:218 | Kichik (GroupBy) |
| 10 | P2 | M-3 Withdrawals list Pending/Rejected | CashRegisterService.cs:87 | Kichik (filtr) |
| 11 | P2 | M-1 Units dropdown route 404 | warehouse/api.ts:112 | Kichik |
| 12 | P2 | M-4 POS "+" no-op | PosPage.tsx:270 | Kichik |
| 13 | P2 | M-5 POS stock stale | PosPage.tsx:61 | Kichik (invalidate) |
| 14 | P3 | M-6 404 body yo'q | ApiControllerBase.cs:49 | Kichik |
| 15 | P3 | L-1 Debt summary UTC boundary | DebtQueryService.cs:137 | Kichik |
| 16 | P3 | L-4 staff-performance 403 | EmployeesPage.tsx:41 | Kichik (`enabled`) |
| 17 | P3 | L-3 Sessions isCurrent | AuthController.cs:112 | Kichik |
| 18 | P3 | L-2 WithdrawType hardcode | CashRegisterService.cs:255 | Kichik |

**Tavsiya qilingan tartib:** P0 (1–4) — biznes-nazorat bypass, xavfsizlik, va butunlay siniq feature'lar; darhol. P1 (5–8) — moliyaviy hisoblash to'g'riligi va auth UX. P2/P3 — correctness/UX polish.

---

## 5. Fixes Applied (2026-07-21)

Barcha 18 amaliy topilma tuzatildi; L-3 ataylab qabul qilindi. Backend `dotnet build` ✓ · frontend `tsc/lint/build` ✓.

| # | Fix |
|---|-----|
| H-1 | `SalePaymentService`: `IMarketSettingsService` inject + `CheckDebtRulesAsync` (regulars + limit) partial-payment Debt yaratishdan OLDIN (to'lov qo'llanishidan avval → rollback toza). |
| H-2…H-7, M-1 | Frontend `api.ts` URL'lari haqiqiy `[action]` route'lariga moslandi (UpdateProduct/DeleteProduct/GetUnits/GetCustomerDebts/ActivateUser/DeactivateUser). Stocktake — backendga `~/api/Products/stocktake` absolute route. |
| H-8 | `TelegramController` webhook `X-Telegram-Bot-Api-Secret-Token` ni `Telegram:WebhookSecret` bilan `FixedTimeEquals` tekshiradi; secret yo'q → fail-closed. **Deploy:** `Telegram__WebhookSecret` env + `setWebhook` secret_token. |
| H-9 | `RequirePermission`/`RequireRole` rad etilganda `<NoAccess>` render qiladi (navigatsiya emas → loop yo'q); `useFirstAccessiblePath` + login/index birinchi ruxsatli sahifaga. |
| H-10 | `ComputeFinancialsAsync` withdrawal oynasi effektiv vaqt `(ApprovedAt ?? WithdrawalDate)` bo'yicha. |
| H-11 | `ComputeFinancialsAsync` payments/sales/withdrawals `s.UserId` (smena kassiri) bo'yicha filtrlanadi. |
| H-12 | `UpdateMyProfile` noto'g'ri joriy parol → 400 (401 emas → refresh-interceptor ishlamaydi). |
| H-13 | Dashboard/Reports `format(...,'yyyy-MM-dd')` (Tashkent kalendar sanasi) yuboradi. |
| M-2 | `GetDebtorsAsync` `GroupBy(CustomerId)` + `Sum` — mijozga bitta qator, jami qarz. |
| M-3 | `GetCashRegisterAsync` withdrawals ro'yxati faqat NotRequired/Approved. |
| M-4 | POS «+» cart-line data'dan qo'shadi (qidiruvga bog'liq emas). |
| M-5 | POS cart-mutation'lar `['pos-products']` invalidate qiladi (stock yangilanadi). |
| M-6 | `ApiControllerBase` NotFound endi `{ message }` tashiydi. |
| L-1 | `GetSummaryStatsAsync` Tashkent kun/oy chegaralari (`ITashkentClock`). |
| L-2 | `RequestWithdrawalAsync` `request.WithdrawType` (cash/click) ni hurmat qiladi. |
| L-4 | `EmployeesPage` staff-performance query `reports.access` bilan gated. |
| L-3 | **Qabul qilindi (won't-fix):** to'g'ri fix refresh-token'ni GET'ga qo'yishni talab qiladi (transit/log oshkoralik xavfi); best-effort "это устройство" label qoldirildi — xavfsizlik ta'siri yo'q. |

---
*Report tayyorlandi: har topilma haqiqiy fayl bilan solishtirib tasdiqlangan. PLAUSIBLE deb belgilangan 4 ta (H-12, H-13, L-2, M-6) topilma kod-faktlari tasdiqlangan, lekin failure-scenariy in-app trigger'i to'liq isbotlanmagan — verify qo'shimcha tekshiruvni oshkor qilgan. Barcha fix'lar 2026-07-21 da qo'llanib verify qilindi.*
