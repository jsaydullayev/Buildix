# Money-movement integrity & fraud audit (P0)

Roadmap themasi «Money-movement integrity & fraud audit» bo'yicha bajarilgan ish.
Manba: `docs/` next-directions surveylari (2026-07-21). Barcha o'zgarishlar
**additiv** — Flutter klient buzilmaydi (HTTP route'lar o'zgarmagan, faqat
controller→service imzolari ichki). Backend build ✓ · **34/34 test ✓**.

## 1. Fraud audit izi — «kim qildi» endi yoziladi

Qiymatni o'zgartiruvchi savdo amallariga aktor `userId` (JWT claim) + audit-log
qatori qo'shildi. Ilgari bu amallar **hech qanday iz qoldirmasdan** ishlagan —
ichki firibgarlikning asosiy vektorlari (soxta qaytarish + naqdni cho'ntakka,
qarz-savdoda narx/chegirmani tushirish).

| Amal | Audit action | Payload |
|------|--------------|---------|
| `SaleReversalService.DeleteSaleAsync` | `Delete` | SaleNumber, seller, customer, prevStatus, total, paid, reversedCash |
| `SaleReversalService.ReturnSaleItemAsync` | `Return` | saleItemId, returnQty, refundAmount, isFullReturn, total, paid |
| `SaleItemService.UpdateSaleItemPriceAsync` | `PriceOverride` | saleItemId, old→new price, status |
| `SaleService.SetSaleDiscountAsync` | `Discount` | old→new discount, status |
| `SaleService.MarkSaleAsDebtAsync` | `MarkDebt` | customer, remainingDebt, dueDate |

- Yangi `AuditActions`: `Return`, `Discount`, `PriceOverride`, `MarkDebt`
  (`Buildix.Domain/Constants/AuditEvents.cs`).
- Audit qatori `EnqueueActionAsync` bilan **biznes-yozuv bilan bir transaction'da**
  commit/rollback bo'ladi (yarim-holat qolmaydi).
- `CancelSaleAsync` allaqachon `Cancel` audit qilardi — o'zgarmadi.
- Aktor har doim **JWT claim'idan** olinadi (`ClaimTypes.NameIdentifier`), hech
  qachon request body'dan emas — soxtalashtirib bo'lmaydi. Controller yo'q bo'lsa
  `Unauthorized()`.

**Ataylab qamrab olinmadi:** `RemoveSaleItemAsync` va `AddSaleItemAsync` (faqat
Draft savat qurish, yuqori chastota, hali commit bo'lmagan). Ularni auditlash
audit-log'ni ko'mib tashlardi va — muhimi — `Delete` action'ini ishlatish
bulk-delete burst-detektorini (5 delete/10 daqiqa) har oddiy savat tahririda
noto'g'ri ishga tushirardi.

## 2. Pul-poyga locklari (FOR UPDATE)

`Sale` entity'da xmin concurrency token **ataylab o'chirilgan**, shu sababli
`SalePaymentService.AddPaymentAsync` `SELECT *, xmin ... FOR UPDATE` lock oladi.
Ammo `PaidAmount` + kassa balansini o'zgartiruvchi reversal yo'llari bu lock'ni
**olmasdi** — parallel to'lov+qaytarish `PaidAmount`'ni lost-update qilib, till'ni
ikki marta o'zgartirishi mumkin edi.

Endi bir xil lock quyidagilarda ham olinadi (`SaleReversalService.LockSaleForUpdateAsync`):
- `CancelSaleAsync`
- `DeleteSaleAsync`
- `ReturnSaleItemAsync`

Lock PostgreSQL-only; InMemory test-provider'da o'tkazib yuboriladi (`ProviderName`
tekshiruvi). To'lov yo'li bilan bir xil, tranzaksiya oxirida avtomatik bo'shaydi.

## 3. Chek-raqam to'qnashuvsiz (ЧЕК № / №)

`SaleNumber` va `ZakupReceipt.ReceiptNumber` ilgari non-unique indeksda `MAX+1`
bilan berilardi — kodda «rare concurrent-create race may reuse a number» deb
tan olingan; moliyaviy hujjat uchun haqiqiy audit muammosi.

Yechim: `MarketSequenceLock.AcquireAsync` — **tranzaksiya-doirasidagi
`pg_advisory_xact_lock(class, marketId)`** raqam-allokatsiyasini market bo'yicha
serializatsiya qiladi. Raqam allokatsiyasi endi transaction **ichida**, lock
ostida bajariladi (`SaleService.CreateSaleAsync`, `ZakupService.CreateZakupReceiptAsync`).

- **Migratsiya kerak emas** — indeks non-unique qoladi (unique qilib data
  migratsiya qilish mavjud dublikatlarda fail bo'lishi mumkin edi; advisory lock
  allokatsiyada to'qnashuvni butunlay oldini oladi). PostgreSQL-only, InMemory'da no-op.
- Lock class'lari: `SaleNumberClass=1`, `ZakupReceiptNumberClass=2` (bir-biri bilan
  contend qilmasin).

## 4. Test-loyihasi tuzatildi (avvaldan buzilgan)

`Buildix.Tests` `IMarketSettingsService` va `IHttpContextAccessor` servislarga
qo'shilgach yangilanmagan edi — oxirgi build faqat `Buildix.API`'ni kompilyatsiya
qilgani uchun sezilmagan. `TestHarness` (permissive settings substitute) va
`AuthSubscriptionTests` tuzatildi. Endi butun solution kompilyatsiya bo'ladi,
**34/34 test o'tadi**.

## 5. Idempotency-Key — double-submit himoyasi (backend to'liq)

Double-submit (double-click, mobil retry, proxy replay) qisman to'lovlar va naqd
yechishlarni ikkilantirishi mumkin edi. Endi backend `Idempotency-Key` header
kontraktini qo'llab-quvvatlaydi.

**Arxitektura:**
- `IdempotencyRecord` jadval (`MarketId, Scope, Key` unique indeks) + migratsiya
  `20260721213554_AddIdempotencyRecords` (to'liq additiv — faqat yangi jadval).
- `IIdempotencyService` (Application) / `IdempotencyService` (Infrastructure —
  Npgsql 23505 unique-violation'ni aniqlaydi).
- `[Idempotent("scope")]` action-filter (`Buildix.API/Filters`).

**Oqim** (`Idempotency-Key` header bo'lganda):
1. Filter (market, scope, key) uchun **pending** qator INSERT qiladi (unique indeks).
   - INSERT muvaffaqiyatli → **Proceed**: action ishlaydi, 2xx javob saqlanadi.
   - Unique-violation → mavjud qator o'qiladi:
     - Tugagan (2xx) → **Replay** (saqlangan javob, action qayta ishlamaydi).
     - Hali ishlayotgan → **409 InProgress** (klient qayta uradi).
     - Payload boshqacha (hash mos emas) → **422 PayloadMismatch**.
2. Non-2xx natija → claim o'chiriladi (qonuniy retry ishlashi uchun) — muvaffaqiyatsizlik hech qachon doimiy saqlanmaydi.
3. Abandoned pending (>120s, ilova crash bo'lgan) atomik `UPDATE...WHERE` bilan qayta-claim qilinadi — kalit hech qachon abadiy bloklanmaydi.

**Qamrab olingan endpointlar:** `POST Sales/{id}/payments` (`sale-payment`),
`POST Debts/{id}/pay` (`debt-payment`), `POST CashRegister/withdraw` (`cash-withdraw`),
`/withdraw-request` (`cash-withdraw-request`), `/add` (`cash-add`).

**⚠️ Klient-tomon talabi (KEYINGI QADAM):** Bu **opt-in** — header bo'lmasa
xatti-harakat avvalgidek. To'liq himoya uchun **web (axios) va Flutter** har
mantiqiy operatsiya uchun bitta barqaror `Idempotency-Key` (masalan `crypto.randomUUID()`)
generatsiya qilib, retry'da **aynan o'sha** kalitni qayta yuborishi kerak. Kalit
yuborilmaguncha backend bloklanmaydi, lekin himoya ham yo'q.

> **Eslatma:** #2 dagi FOR UPDATE lock + mavjud over-payment guard to'liq-to'lov
> double-submit'ini allaqachon bloklaydi; idempotency qoldiq xavfni (qisman
> to'lovlar, naqd yechishlar, yechish-so'rovlari) yopadi.

---

## Keyingi inkrement uchun (ataylab kechiktirilgan)

### Shared-drawer smena modeli
Bitta kassaga (bir market = bitta `CashRegister`) parallel kassirlar «phantom
discrepancy»ni kafolatlaydi. Tuzatish (single-active-shift-per-register yoki
per-cashier float) — moliyaviy hisob-kitob xatti-harakatini o'zgartiradi, shu
sababli **avval Shift test-safety-net'i ostida** qilinishi kerak (survey
sequencing shuni tavsiya qiladi). Bu yerda o'zgartirilmadi.
