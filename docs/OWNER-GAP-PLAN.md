# Owner (egasi) paneli — kamchiliklar va to'ldirish rejasi

**Maqsad:** Owner panelidagi bo'shliqlarni yopish — backendda tayyor turgan, lekin UI'ga ulanmagan funksiyalarni ishga tushirish.

**Manba:** Owner bo'limining barcha sahifalarini (`dashboard`, `sales`, `warehouse`, `debts`, `purchases`, `shifts`, `reports`, `employees`, `settings`, `account`, `pos`) kod bo'yicha tekshirish + `Buildix.API/Controllers` dagi barcha endpointlarni frontend chaqiruvlari bilan solishtirish. Sana: **2026-07-23**.

**Umumiy xulosa:** Owner paneli **o'qish tomonidan deyarli to'liq** — panel, sotuvlar, ombor, qarzlar, smenalar, hisobotlar ishlaydi. **Boshqaruv tomoni esa yarim qolgan**: tovar kirimi, mijozlar, yetkazib beruvchilar, ruxsatlar, audit va eksportlar — hammasining backendi tayyor, ustiga UI qo'yilmagan. 21 ta kamchilikdan **20 tasi faqat frontend ishi**; backendga qo'shish kerak bo'lgan yagona narsa — boshqa kassirning smenasini majburiy yopish.

---

## 1. Kamchiliklar reyestri

Belgilar: 🔴 bloklovchi · 🟠 muhim · 🟡 o'rta · ⚪ kichik.
"Backend" ustuni — endpoint allaqachon bormi.

| ID | Kamchilik | Og'irlik | Backend | Bosqich |
|---|---|---|---|---|
| O-1 | Xarid (tovar kirimi) yaratib bo'lmaydi — "Yangi xarid" tugmasi o'lik | 🔴 | ✅ `POST /Zakups` | B1 |
| O-2 | Ruxsatlarni boshqarish UI yo'q — RBAC amalda ishlamaydi | 🔴 | ✅ `GET/PUT /Users/{id}/permissions` | B2 |
| O-3 | Mijozlar bo'limi yo'q (sahifa ham, marshrut ham) | 🟠 | ✅ `CustomersController` (to'liq CRUD) | B3 |
| O-4 | Xodimni tahrirlab/o'chirib bo'lmaydi (faqat blok) | 🟠 | ✅ `PUT/DELETE /Users/{id}` | B2 |
| O-5 | Eksport tugmalari deyarli hech qayerda yo'q | 🟠 | ✅ 11 ta export endpointi | B4 |
| O-6 | Sotuvni bekor qilish / tovar qaytarish UI yo'q | 🟠 | ✅ `POST /Sales/{id}/cancel`, `/return-item` | B4 |
| O-7 | Audit jurnali ko'rinmaydi | 🟠 | ✅ `GET /AuditLogs`, `/AuditLogs/suspicious` | B5 |
| O-8 | Yetkazib beruvchilar CRUD yo'q | 🟠 | ✅ `SuppliersController` | B1 |
| O-9 | Xarid chekining ichi ochilmaydi + yetkazib beruvchiga to'lov yo'q | 🟠 | ✅ `GET /Zakups/{id}` + to'lov | B1 |
| O-10 | Boshqa kassirning ochiq smenasini yopib bo'lmaydi | 🟠 | ❌ **yangi endpoint kerak** | B5 |
| O-11 | Sotuvlarda sotuvchi bo'yicha filtr yo'q | 🟡 | ✅ `sellerId` query param | B4 |
| O-12 | Kategoriyalar CRUD yo'q | 🟡 | ✅ `ProductCategoriesController` | B3 |
| O-13 | Qarz muddatini o'zgartirib bo'lmaydi | 🟡 | ✅ `PUT /Debts/{debtId}/due-date` | B4 |
| O-14 | Tovar rasmi va Excel import yo'q | 🟡 | ✅ `/{id}/image`, `/import/preview|confirm` | B6 |
| O-15 | Butun ro'yxat 4 ta raqam uchun yuklanadi (ombor, xarid) | 🟡 | ❌ yig'indi endpointi kerak | B6 |
| O-16 | Xodim kartasidagi raqamlar qaysi davrniki — noma'lum | 🟡 | ✅ `period` param | B2 |
| O-17 | Smenalar tarixi 20 ta bilan cheklangan, filtr/sahifalash yo'q | 🟡 | ⚠️ `limit` bor, filtr yo'q | B5 |
| O-18 | Qarzdorlar ro'yxati sahifalanmagan | 🟡 | ❌ paging yo'q | B6 |
| O-19 | Do'konning standart tili sozlanmaydi | ⚪ | ✅ `MarketSettings.defaultLanguage` | B6 |
| O-20 | Owner uchun bildirishnomalar sahifasi yo'q | ⚪ | ⚠️ sotuvchida bor | B6 |
| O-21 | "Bugun faol" brauzer kunidan hisoblanadi (Toshkent emas) | ⚪ | — | B6 |

---

## 2. Bosqichlar

### B1 — Xarid zanjiri (🔴 birinchi navbatda)

**Nega birinchi:** hozir omborga tovar kirimini tizim orqali kiritishning **umuman yo'li yo'q** — qoldiqlar faqat inventarizatsiya (`stocktake`) bilan tuzatiladi. Bu do'konni real ishlatishga to'sqinlik qiladigan yagona kamchilik.

| Ish | Fayl | API |
|---|---|---|
| **O-1** `NewPurchaseModal` — yetkazib beruvchi tanlash/yaratish, tovar qatorlari (mahsulot, miqdor, kelish narxi), to'lov holati | yangi `features/purchases/NewPurchaseModal.tsx` | `POST /Zakups` |
| "Yangi xarid" tugmasini ulash | `PurchasesPage.tsx:69` — hozir `onClick` siz `<Button>` | — |
| **O-9** `ReceiptDetailModal` — chek ichidagi tovarlar, yetkazib beruvchi, to'lov tarixi; qator bosiladigan bo'lsin | yangi + `PurchasesPage.tsx:149` (`hover:bg` bor, lekin bosilmaydi — foydalanuvchini aldaydi) | `GET /Zakups/{id}` |
| Yetkazib beruvchiga to'lov | o'sha modal | `POST /Zakups/{id}` |
| **O-8** Yetkazib beruvchilar boshqaruvi (ro'yxat, qo'shish, tahrirlash, o'chirish) | yangi `features/suppliers/` + `/:subdomain/suppliers` marshruti | `SuppliersController` (CRUD + `{id}/delete-info`) |

**Qabul mezoni:** Owner yangi xarid kiritgach, ombordagi qoldiq va tannarx yangilanadi; xarid cheki ro'yxatda ko'rinadi va ochiladi; yetkazib beruvchi qarzi to'lov bilan kamayadi.

---

### B2 — Rollar va xodimlar (🔴/🟠)

**Nega:** `RequirePermission` tizimi butun backendni qoplaydi (`data.costPrice`, `data.profit`, `data.allSalesView`, `sales.delete`…), lekin uni **boshqaradigan UI yo'q** — ya'ni rollar tizimi qog'ozda qolgan. Admin'ga foyda ko'rish huquqini berish yoki sotuvchidan tortib olish imkoni yo'q.

| Ish | Fayl | API |
|---|---|---|
| **O-2** `PermissionsModal` — xodim bo'yicha ruxsatlar ro'yxati, guruhlangan (sotuv/ombor/qarz/kassa/ma'lumot), toggle bilan | yangi `features/employees/PermissionsModal.tsx` | `GET/PUT /Users/{id}/permissions` |
| **O-4** `EditEmployeeModal` — ism, telefon, rol, parolni tiklash | yangi | `PUT /Users/{id}` |
| **O-4** Xodimni o'chirish (tasdiqlash bilan) | `EmployeesPage.tsx` | `DELETE /Users/{id}` |
| **O-16** Davr tanlagich (bugun/hafta/oy/yil) + kartada davr yozuvi | `EmployeesPage.tsx`, `employees/api.ts:37` (hozir qat'iy `period: 'month'`) | `GET /Reports/staff-performance?period=` |

**Qabul mezoni:** Owner yangi Admin yaratib, unga `data.profit` berishi va keyin qaytarib olishi mumkin; o'zgarish darhol kuchga kiradi (foydalanuvchi qayta login qilgach yoki token yangilangach).

**Diqqat:** Owner o'zining `users.manage` ruxsatini yo'qotib qo'ymasligi uchun UI o'z-o'ziga ruxsat bermasin (Owner roli allaqachon `hasPermission` da `true` qaytaradi, lekin modal Owner uchun ochilmasin).

---

### B3 — Mijozlar va kategoriyalar (🟠/🟡)

| Ish | Fayl | API |
|---|---|---|
| **O-3** Mijozlar sahifasi: ro'yxat (qidiruv, tur bo'yicha filtr), qo'shish, tahrirlash, qarz limiti, "doimiy mijoz" belgisi, o'chirish | yangi `features/customers/CustomersPage.tsx` (`features/customers/api.ts` allaqachon yozilgan), `app/router.tsx` ga `customers` marshruti, `shared/config/navigation.ts` ga menyu bandi | `CustomersController`: paged, create, update, delete, `{id}/delete-info`, `{id}/soft-delete` |
| Mijoz kartasi: uning qarzlari va sotuvlari tarixi | o'sha sahifa | `GET /Debts/GetCustomerDebts/{id}`, `GET /Sales?search=` |
| **O-12** Kategoriyalar boshqaruvi (qo'shish, nomini o'zgartirish, o'chirish) | `features/warehouse/` ichida modal yoki Sozlamalarda bo'lim | `ProductCategoriesController` |

**Qabul mezoni:** Owner mijoz qo'shib, unga qarz limiti belgilay oladi; kassada o'sha mijoz qidiruvda chiqadi. Yangi kategoriya bazaga kirmasdan yaratiladi.

---

### B4 — Sotuvlar bilan ishlash va eksportlar (🟠)

| Ish | Fayl | API |
|---|---|---|
| **O-6** Chekni bekor qilish (tasdiqlash + sabab) — omborga qaytaradi, naqdni kassadan yechadi | `features/sales/SaleDetailModal.tsx` | `POST /Sales/{saleId}/cancel` |
| **O-6** Tovar qaytarish (qatordan, qisman miqdor bilan) | o'sha modal | `POST /Sales/{saleId}/return-item` |
| **O-11** Sotuvchi bo'yicha filtr (select) | `features/sales/SalesPage.tsx`, `sales/api.ts` (`sellerId` yuborilmayapti) | `GET /Sales?sellerId=` |
| **O-13** Qarz muddatini o'zgartirish | `features/debts/DebtsPage.tsx` | `PUT /Debts/{debtId}/due-date` |
| **O-5** Eksport tugmalari — quyidagi jadval | har sahifaning sarlavhasida | pastda |

**O-5 — ulanmagan eksportlar** (hozir UI'da faqat **2 tasi** bor: Qarzlar Excel va Hisobot davri PDF):

| Sahifa | Endpoint | Format |
|---|---|---|
| Sotuvlar | `GET /Sales/export` | Excel |
| Sotuvlar | `GET /Sales/export-pdf` | PDF |
| Ombor | `GET /Products/export` | Excel |
| Mijozlar | `GET /Customers/export` | Excel |
| Yetkazib beruvchilar | `GET /Suppliers/export` | Excel |
| Xaridlar | `GET /Zakups/export` | Excel |
| Hisobotlar | `GET /Reports/comprehensive-report/export` | Excel |
| Hisobotlar | `GET /Reports/inventory-report/export` | Excel |
| Hisobotlar | `GET /Reports/daily/export` | Excel |
| Hisobotlar | `GET /Reports/daily/export-pdf` | PDF |
| Hisobotlar | `GET /Reports/comprehensive/export-pdf` | PDF |

Yuklab olish naqshi tayyor — `DebtsPage.tsx:32-45` dagi blob→link kodini umumiy helperga (`shared/lib/download.ts`) chiqarib, hamma joyda qayta ishlatish kerak. `sales.export` ruxsati allaqachon mavjud, faqat tugma yo'q.

**Qabul mezoni:** har ro'yxat sahifasida eksport tugmasi bor va u joriy filtrlarni hisobga oladi; bekor qilingan chek ro'yxatda `Cancelled` bo'lib ko'rinadi va hisobotlarga kirmaydi.

---

### B5 — Nazorat: audit va smenalar (🟠)

| Ish | Fayl | API |
|---|---|---|
| **O-7** Audit jurnali sahifasi: kim / qachon / nima qildi; amal turi, foydalanuvchi va sana bo'yicha filtr | yangi `features/audit/` + `/:subdomain/audit` marshruti (Owner-only) | `GET /AuditLogs` |
| **O-7** "Shubhali amallar" ajratilgan ko'rinish | o'sha sahifa, alohida tab | `GET /AuditLogs/suspicious` |
| **O-10** Boshqa kassirning ochiq smenasini majburiy yopish | `features/shifts/ShiftsPage.tsx` + **backend** | ❌ yangi: `POST /Shifts/{id}/force-close` |
| **O-17** Smenalar tarixiga sahifalash + sana/kassir filtri | `ShiftsPage.tsx`, `shifts/api.ts:102` (hozir `limit=20`, filtr yo'q) | `GET /Shifts` ni kengaytirish |

**O-10 backend ishi:** hozir `POST /Shifts/close` faqat **chaqiruvchining o'z** smenasini yopadi (`ShiftsController.cs:47`, `CurrentUserId()` dan oladi). Kassir smenani yopmasdan ketsa, u abadiy ochiq qoladi — `SalesOnlyWhenShiftOpen` qoidasi va kassa hisob-kitobi buziladi. Kerak:
- `POST /Shifts/{shiftId}/force-close`, `[RequirePermission(PermissionKeys.UsersShift)]`
- Sanoq naqdni Owner kiritadi; farq (`discrepancy`) odatdagidek yoziladi
- Audit yozuvi: kim majburiy yopdi, qaysi kassirning smenasi, farq qancha

**Qabul mezoni:** Owner audit jurnalidan "kim chekni bekor qildi / chegirma berdi / kassadan pul yechdi" ni ko'ra oladi; unutilgan smenani yopib, farqni qayd eta oladi.

---

### B6 — Sayqal va samaradorlik (🟡/⚪)

| Ish | Tafsilot |
|---|---|
| **O-15** | `WarehousePage` `productsApi.listAll` ni, `PurchasesPage` `allReceipts` ni **faqat 4 ta statistika kartasi uchun** to'liq tortadi va clientda yig'adi. Ming pozitsiyali do'konda har sahifa ochilishida og'ir so'rov. Yechim: server tomonda yig'indi endpointi (`GET /Products/summary`, `GET /Zakups/summary`) |
| **O-18** | Qarzdorlar ro'yxati sahifalanmagan — `GET /Debts/debtors` hammasini bir marta qaytaradi |
| **O-14** | Tovar rasmi (`POST/DELETE /Products/{id}/image`) — `Product.imageUrl` DTO'da bor, hech qayerda ko'rsatilmaydi; Excel import (`POST /Products/import/preview` → `/import/confirm`) — ikki bosqichli sehrgar kerak |
| **O-19** | Sozlamalarga do'konning standart tili (`MarketSettings.defaultLanguage`) — DTO'da bor, saqlanadi, maydoni yo'q. Yangi xodim uchun standart til. Shaxsiy til allaqachon qo'shilgan (`settings.language`) — ikkalasini adashtirmaydigan qilib joylash kerak |
| **O-19** | Shuningdek `AuditEnabled` va `InactivityLogoutMinutes` sozlamalari ham DTO'da bor, UI'da ko'rsatilmaydi |
| **O-20** | Owner uchun bildirishnomalar (kam qolgan tovar, muddati o'tgan qarz) — sotuvchida `seller/notifications` bor, Owner panelida yo'q |
| **O-21** | `EmployeesPage.tsx:58` — "bugun faol" `toDateString()` bilan brauzer kunidan hisoblanadi; qolgan hamma joyda Toshkent kuni (`LocalDayToUtcRange`) ishlatiladi |

---

## 3. Backendga qo'shiladigan ishlar (jami 4 ta)

Qolgan hamma narsa faqat frontend. Backendda yangi kod kerak bo'ladigan joylar:

1. **`POST /Shifts/{shiftId}/force-close`** — O-10, yuqorida batafsil.
2. **`GET /Products/summary`** va **`GET /Zakups/summary`** — O-15, yig'indi kartalar uchun (pozitsiya soni, qoldiq qiymati, kam/tugagan; oylik xarid soni va summasi).
3. **`GET /Debts/debtors` ga sahifalash** — O-18, `PagedResult<DebtorSummary>` ga o'tkazish.
4. **`GET /Shifts` ga filtr** — O-17, sana oralig'i va `userId` parametrlari.

---

## 4. Tavsiya etilgan tartib

```
B1 (xarid zanjiri)      ← do'kon tizimni real ishlata olishi uchun shart
   ↓
B2 (rollar/xodimlar)    ← RBAC tizimini jonlantiradi
   ↓
B3 (mijozlar)           ← qarz va sotuv zanjiri to'liq bo'ladi
   ↓
B4 (eksport + bekor)    ← kundalik ish qulayligi
   ↓
B5 (audit + smena)      ← nazorat
   ↓
B6 (sayqal)             ← samaradorlik va mayda-chuydalar
```

Har bosqich mustaqil yetkazib beriladi: B1 tugagach do'kon undan foydalana boshlaydi, keyingisini kutmaydi.

---

## 5. Har bosqich uchun "tayyor" mezoni

- `npx tsc -b --noEmit` va `npx eslint src` toza
- `dotnet build` + `dotnet test` o'tadi (backend tegilgan bo'lsa)
- Yangi matnlar **uchala** locale'da (`ru.ts` — manba sxema, `uz.ts`/`en.ts` bir xil kalitlar bilan; aks holda `tsc` sinadi)
- Yangi sahifa/amal ruxsat bilan himoyalangan (`perm(...)` marshrutda + `hasPermission` UI'da) va backend ham o'sha kalitni tekshiradi
- Pul yoki qoldiqni orqaga qaytaradigan amallar (bekor qilish, qaytarish, majburiy yopish) — tasdiqlash oynasi bilan va audit yozuvi bilan
- O'lchov, sana, summa formatlash — mavjud helperlar orqali (`unitLabel`, `formatSum`, `formatQty`, Toshkent kuni)

---

## 6. Ilova — tekshiruv metodikasi

Ro'yxat taxminlarga emas, ikki tomonlama solishtirishga asoslangan:

1. `Buildix.API/Controllers/*` dagi barcha `[Http*]` marshrutlari yig'ildi.
2. `Buildix.Web/src` dagi barcha `apiClient.*()` chaqiruvlari yig'ildi.
3. Ikki ro'yxat farqi = "backendda bor, UI'da yo'q" (A bo'limi).
4. Har Owner sahifasi alohida o'qib chiqildi — `onClick` siz tugmalar, bosilmaydigan qatorlar, qat'iy parametrlar va client tomonda yig'ilayotgan og'ir so'rovlar qidirildi (B bo'limi).
