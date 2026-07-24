# Admin dizayn — qolgan ishlar rejasi

**Konteks:** A1–A9 bosqichlari bajarildi (3 yangi ekran + BE-1…BE-11 + navigatsiya, `feature/owner-gap-fill`). Ushbu reja — TZ (`ADMIN-DESIGN-INTEGRATION-TZ.md`) dagi **«moslash» (🟠) bandlaridan** kod bilan tekshirilganda **hali bajarilmagan** qismlarni jamlaydi. Sana: 2026-07-24.

> **Muhim:** qolgan **barcha ishlar faqat frontend** — yangi backend endpoint, entity yoki migration **kerak emas**. Har bir band uchun backend allaqachon tayyor (quyida "BE" ustunida ko'rsatilgan).

---

## Bosqichlar

### R1 — Панель (Dashboard) to'liq dizaynga keltirish 🟠 (§2.1) — eng ko'rinarli
Joriy: 4 KPI ✅ + «Требует внимания» ✅. Dizaynda yana 3 blok bor, hozir yo'q:

| # | Ish | BE tayyorligi |
|---|---|---|
| R1.1 | «Все продажи · сегодня» jadvaliga **ДЕЙСТВИЯ** ustuni — qator amallari: **Изменить чек** (SaleDetailModal'ni ochish) / **Удалить чек → Вернуть** | ✅ `SaleDetailModal` + `/Sales/{id}/cancel` + Возвраты deep-link bor |
| R1.2 | **«Закупы»** jadvali (oxirgi xaridlar, status: приёмка/в пути/черновик) + «Отметить принятым» | ✅ `deliveryStatus` + `/Zakups/{id}/accept` (A4) |
| R1.3 | O'ng panel **«Доступы продавцов»** — kassir ruxsatlarining tez ko'rinishi (read-only chiplar yoki 4 toggle → Сотрудники sahifasiga link) | ✅ `employeesApi.list()` permissions/limitlar bilan (A6) |

**Tavsiya:** R1.3 ni read-only ko'rinish + «Настроить →» link qilib soddalashtirish (toggle'ni ikki joyda saqlamaslik uchun — haqiqiy tahrir Сотрудники sahifasida).

---

### R2 — Продажи (Sales) dizayn-fidelity 🟠 (§2.2)
Funksional to'liq ishlaydi (period/sotuvchi/qidiruv bor), lekin ko'rinish dizayndan farq qiladi:

| # | Ish | BE |
|---|---|---|
| R2.1 | Period toggle: `today/week/month` → dizayndagi **Сегодня / Вчера / Все** (Вчера + Все qo'shish, Неделя/Месяц o'rniga yoki yoniga) | ✅ `GET /Sales?period=` |
| R2.2 | Sotuvchi filtri: `<select>` dropdown → **chiplar** (Все / har sotuvchi) | ✅ `sellerId` param |

---

### R3 — Клиенты (Clients) filtr chiplari 🟠 (§2.7)
| # | Ish | BE |
|---|---|---|
| R3.1 | Chiplar qo'shish: **Все / С долгом / Организации** (client-side filter: `totalDebt > 0`, `customerType === 'Legal'`) | ✅ `customerType` + `totalDebt` maydonlari bor |

---

### R4 — Долги (Debts) banner ⚪ (§2.8)
| # | Ish | BE |
|---|---|---|
| R4.1 | Detal modalda **«У клиента есть ещё долги»** banneri (mijozning boshqa ochiq cheklari bo'lsa) | ✅ `debtsApi.debtors` — mijoz bo'yicha cheklar soni bor |

Chiplar (Все/Просрочены/Срок сегодня) **allaqachon bor** — faqat banner qoladi.

---

## Rejadan tashqari (ataylab qoldirilgan — qaror kerak)

| Band | Sabab |
|---|---|
| **§2.16 «Язык интерфейса»** Account'da | Foydalanuvchining **parallel til-refactoringi** (~26 fayl, uncommitted) — u bilan aralashmaslik uchun tegilmadi. Refactoring tugagach ulanadi. |
| **§6.6 Admin-kod override** | «Изменение цены»/«Возвраты» o'chiq bo'lsa kassir admin-kod bilan bir martalik amal. v1 da toggle-off = butunlay bloklangan. Override-kod = yangi mexanizm (modal + backend tekshiruv + audit) — **ochiq savol, qaror kutmoqda**. |
| **§6.4 Посещаемость sozlamasi** | Reja/kechikish (08:00–20:00 / 08:15) hozir konstanta. `MarketSettings`ga chiqarish — **ochiq savol**. |
| **§2.10 To'liq sahifa xarid** | Modal qoldi — TZ modalga ruxsat beradi ("yoki modalda saqlab"). Bo'shliq emas, ixtiyoriy yaxshilash. |

---

## «Tayyor» mezoni (har band)
- `npx tsc --noEmit` + `npx eslint` toza.
- Yangi matnlar **uchala** locale'da (ru manba, uz/en bir xil kalit).
- Browser'da tekshirish (playwright) — 0 console/HTTP xato, dizaynga moslik.
- Pul/qoldiq qaytaruvchi amal (R1.1 «Удалить чек») — mavjud tasdiqlash + audit yo'li orqali.
- Commit `Jahongir Saydullayev` nomida, Claude atributsiz.

## Taxminiy hajm
- **R1** — eng katta (Dashboard, 3 blok) · **R2/R3/R4** — kichik-o'rta.
- Backend/migration **yo'q** → risk past, faqat UI + i18n.
- Tavsiya etilgan tartib: **R1 → R3 → R2 → R4** (ko'rinarlilik bo'yicha).

---

## ✅ HOLAT (2026-07-24): R1–R4 BAJARILDI

| Bosqich | Commit | Izoh |
|---|---|---|
| R1 Панель | `a3204f7` | «Все продажи» amallari (row→SaleDetailModal) + «Закупы» + «Доступы продавцов» |
| R3 Клиенты | `11b638c` | Chiplar Все/С долгом/Организации — **kichik additiv backend param** (server-filter, pagination to'g'ri) |
| R2 Продажи | `ac10844` | Period Сегодня/Вчера/Все + sotuvchi chiplari |
| R4 Долги | `794357c` | «У клиента есть ещё долги» banneri |

Hammasi browser'da tekshirildi (0 xato), 37/37 backend test o'tdi, tsc/eslint/vite toza, commitlar jsaydullayev nomida Claude-siz.

**Eslatma:** R3 dastlab «frontend only» deb belgilangandi, lekin paged endpoint faqat `search`ni qo'llab-quvvatlagani uchun to'g'rilik (pagination) uchun kichik additiv backend filtr param qo'shishga to'g'ri keldi (default null = filtrsiz).

**Qolgan (ataylab — qaror kutmoqda):** §2.16 til tanlash (parallel refactoring), §6.6 admin-kod override, §6.4 Посещаемость sozlamasi.

---

## ✅ YAKUNIY YOPISH (2026-07-24)

| Ish | Commit | Holat |
|---|---|---|
| O'lik scaffold kod (`_shared/ComingSoonPage`, `PagePlaceholder`) | `e6ab6d1` | ✅ olib tashlandi |
| §6.4 Посещаемость grafigi → `MarketSettings` (WorkDayStart/End/LateThreshold, sozlanadigan) | `69dfcc3` | ✅ backend+UI, migration additiv, 37/37 test |

**Qaror qabul qilingan (endi ochiq savol emas):**
- **§6.6 admin-kod override** — foydalanuvchi qarori: **qilinmaydi**. v1 standart (toggle o'chiq = amal bloklangan, kodsiz) yetarli — u BE-6'da allaqachon bajarilgan. Override — kelajakdagi v2 ixtiyoriy yaxshilanishi.
- **§2.16 til tanlash Account'da** — til switch **Settings sahifasida allaqachon bor** (RU/UZ/EN, butun ilova bo'ylab). Account'ga qo'shish foydalanuvchining parallel til-refactoringiga bog'liq — u tugagach ulanadi.

**Xulosa:** Owner panel amaliy jihatdan **yakunlangan** — barcha ekranlar, dizayn integratsiyasi (A1–A9 + R1–R4), va yopiladigan bo'shliqlar tugadi. Yagona qolgani — Account'dagi til (parallel ishga bog'liq).
