# Buildix — loyiha tahlili

> Qurilish mollari do'konlari uchun ko'p ijarali (multi-tenant) ERP.
> Holat: 2026-07-27 · Tarmoq: `feature/owner-gap-fill`

---

## 1. Qisqacha: bu nima

Buildix — bitta serverda **ko'plab do'konga** xizmat qiladigan savdo-hisob tizimi.
Har bir do'kon o'z manzilida ishlaydi (`buildix.uz/toshkonstroy`), o'z xodimlari,
tovarlari, kassasi va hisobotlariga ega bo'ladi va **boshqa do'konning bitta
qatorini ham ko'ra olmaydi**.

Sement, armatura, bo'yoq kabi mollar bilan ishlaydigan do'kon kunlik ishini
to'liq shu yerda yuritadi: sotuv → kassa → qarz → ombor → xarid → hisobot.
Platforma egasi (SuperAdmin) esa alohida konsoldan do'konlarni ochadi, obunani
yuritadi va to'lovlarni qayd qiladi.

**Kim ishlatadi — to'rt xil odam, to'rt xil ekran:**

| Rol | Nima qiladi | Qayerdan kiradi |
|-----|-------------|-----------------|
| **SuperAdmin** | Do'konlarni ochadi, obuna/to'lov, platforma sozlamalari | Yashirin konsol `/_sa/<segment>` |
| **Owner** (do'kon egasi) | Hamma narsa: hisobotlar, foyda, xodimlar, sozlamalar | `/<do'kon>` — admin panel |
| **Admin** | Egadan tashqari deyarli hamma narsa (foyda/audit yopiq) | `/<do'kon>` — admin panel |
| **Seller** (kassir) | Faqat sotuv, smena, qarz, mijoz | `/<do'kon>/seller` — soddalashtirilgan kassa |

---

## 2. Texnik tarkib

```
Buildix.Domain          — entitilar, enumlar, biznes qoidalari (tashqi bog'liqliksiz)
Buildix.Application     — servislar, DTO, interfeyslar (60 ta servis)
Buildix.Infrastructure  — EF Core, PostgreSQL, tashqi integratsiyalar (38 migratsiya)
Buildix.API             — kontrollerlar (20 ta, ~200 endpoint), middleware, fon vazifalari
Buildix.Tests           — 135 ta test (22 ta test fayli)
Buildix.Web             — React 18 + Vite + TypeScript SPA (~27 000 qator, 128 fayl)
```

- **Backend:** .NET 9 · Clean Architecture · ~34 000 qator C#
- **Ma'lumotlar bazasi:** PostgreSQL 17 · EF Core 9 (Npgsql)
- **Frontend:** React + TanStack Query + react-hook-form/zod + Tailwind (CSS-o'zgaruvchili tema)
- **Real vaqt:** SignalR (`/hubs/sales` — chek o'zgarishlari)
- **Hujjatlar:** QuestPDF (PDF), ClosedXML (Excel)
- **Log:** Serilog → konsol + PostgreSQL (`app_logs`, 30 kunlik saqlash)
- **Deploy:** Docker Compose (db + api + nginx), TLS standart, certbot

---

## 3. Loyiha nima qiladi — modullar bo'yicha

### 3.1 Kassa va sotuv

Ikki xil kassa interfeysi bor: **admin kassasi** (`/pos`) va **sotuvchi kassasi**
(`/seller/pos`) — ikkinchisi kunlik ish uchun soddalashtirilgan.

- Tovarni qidirib yoki artikul bo'yicha topib savatga qo'shish
- **Miqdorni qo'lda kiritish** — 100 dona uchun 100 marta «+» bosilmaydi;
  kasr ham mumkin (3.5 kg, 2.5 tonna)
- Chegirma (butun chek bo'yicha), narxni qatorda tahrirlash (torg) — ruxsat bilan
- To'lov usullari: **Naqd · Karta · O'tkazma · Miks · Qarzga**
- **Miks** — bitta chek uch usulga bo'linadi, uchala ulush bitta atomar so'rovda
  yoziladi (chek «qisman to'langan ⇒ qarz» holatidan o'tib ketmaydi)
- **Chek chiqarish** — termal printer (XPrinter, 58/80 mm rulon) uchun PDF
- **Kechiktirilgan cheklar** — chekni «to'xtatib qo'yib», keyin davom ettirish
- Mijozni chekka biriktirish, yangi mijozni **shu yerning o'zida** yaratish
- Sotuvni bekor qilish, qaytarish (SaleReturn), kredit qo'llash

### 3.2 Ombor va tovarlar

- Tovar kartochkasi: nom, artikul, kategoriya, o'lchov birligi (dona/kg/m/tonna/qop…),
  kelish narxi, sotuv narxi, minimal qoldiq, rasm
- **Excel'dan import** — avval ko'rish (preview), keyin tasdiqlash; bir marta 1000 qator
- Excel'ga eksport (tovarlar, kategoriyalar)
- **Inventarizatsiya** (stocktake) — haqiqiy qoldiqni kiritib, farqni yozib qo'yish
- Ombor harakatlari jurnali: kirim, sotuv, sotuv bekor qilinishi, tuzatish
- **Kam qolgan tovarlar** ro'yxati va avtomatik ogohlantirish
- Tovarni «yashirish» — kassirga ko'rinmaydi, lekin hisobot va tarixda qoladi

### 3.3 Xarid (zakup) va yetkazib beruvchilar

- Ko'p qatorli priyomka: yetkazuvchi, nakladnoy raqami, qatorlar, to'lov
- **«Yo'lda»** rejimi — tovar qabul qilinmaguncha ombor to'lmaydi
- To'lov: bir qismi hozir, qolgani yetkazuvchiga qarz
- **Buyurtma tavsiyasi** — kam qolgan tovarlar bo'yicha avtomatik ro'yxat
- Yetkazuvchi kartochkasi: qarz, so'nggi xaridlar (to'lanmagani **qizil**), qarzni yopish

### 3.4 Mijozlar va qarzlar

- Mijoz bazasi, telefon bo'yicha qidiruv, qarz tarixi
- Qarzni to'lash (qisman ham), muddatni o'zgartirish
- Qarz limiti (do'kon sozlamalaridagi umumiy standart yoki mijozga alohida)
- Muddati o'tgan qarzlar bo'yicha ogohlantirish
- Qarzdorlar ro'yxatini Excel'ga chiqarish

### 3.5 Kassa (naqd pul)

- Sotuv va qarz to'lovlari kassaga **avtomatik** tushadi
- Qo'lda: chiqim (kategoriya bilan), inkassatsiya, naqd kiritish
- **Chiqimni tasdiqlash oqimi** — kassir so'raydi, ega tasdiqlaydi/rad etadi
  (do'kon sozlamasi bilan yoqiladi)
- Kassa qoldig'i smena yopilganda solishtiriladi; ruxsat etilgan farq sozlanadi

### 3.6 Smenalar va davomat

- Smena ochish/yopish, majburiy yopish (ega tomonidan)
- Yopishda sanalgan naqd bilan hisoblangan qoldiq solishtiriladi
- **Davomat**: ish kuni boshlanishi, kechikish chegarasi, avtomatik yopish vaqti
- «Sotuv faqat smena ochiq bo'lganda» — sozlama bilan qattiq qoida

### 3.7 Hisobotlar

19 ta hisobot endpointi, PDF va Excel eksporti bilan:

- Kunlik / davriy / kompleks hisobot
- Foyda xulosasi, kassa qoldig'i, haftalik dinamika
- Top tovarlar, kategoriyalar bo'yicha oylik savdo
- **Xodimlar samaradorligi** va «mening natijam» (kassir o'zinikini ko'radi)
- Dashboard xulosasi: KPI kartalari, diagramma, e'tibor talab qiladigan qatorlar

### 3.8 Xodimlar va ruxsatlar

- **42 ta alohida ruxsat kaliti** (`sales.create`, `data.profit`, `zakup.delete`…)
- Rol bo'yicha standart to'plam + har bir xodim uchun qo'lda sozlash matritsasi
- Owner/SuperAdmin barcha tekshiruvlardan o'tadi; Sellerga taqiqlangan kalitlar
  (tannarx, foyda) matritsadan ham berilmaydi
- Xodimni bloklash, parolini tiklash, sessiyalarini uzish

### 3.9 SuperAdmin konsoli

- **Do'kon yaratish** — ariza asosida: nom, login, parol; sub-path do'kon nomidan
  avtomatik yasaladi (`«Тош Кон Строй» → tosh-kon-stroy`), to'liq havola ko'rsatiladi
- Obuna: tariflar (Start/Standard/Pro), to'lovni qayd qilish, muddatni uzaytirish
- **To'lov oldindan ko'rish** — qaysi sanadan qaysi sanagacha uzayishi tanlanishdan oldin
- Do'konni bloklash/blokdan chiqarish, xodimlarni boshqarish
- Platforma sozlamalari: grace muddati, eslatma kuni, to'liq blok kuni, kontaktlar
- Dashboard: MRR, faol/muddati o'tgan do'konlar, yangi arizalar

---

## 4. Ichidagi yengilliklar (kunlik ishni tezlashtiradigan narsalar)

Bular «bor-yo'q» ro'yxat emas — aynan foydalanuvchi vaqtini yoki xatosini
kamaytiradigan yechimlar.

### Kassirga

| Yengillik | Nima beradi |
|-----------|-------------|
| **Miqdorni bosib kiritish** | 100 dona = bitta yozuv, 100 ta klik emas |
| **Kasr miqdor** | 3.5 kg, 2.5 tonna — qop/metr/kg bilan ishlaydigan do'kon uchun |
| **Fokusda matn belgilanadi** | Eski qiymatni o'chirish shart emas, ustiga yoziladi |
| **Bo'sh maydon = bo'sh** | «0» ni o'chirib o'tirmaysiz (barcha son maydonlarida) |
| **«100 000» formatlash** | Summa yozilayotganda o'zi bo'linadi — nol sanash yo'q |
| **Miks to'lovda «qoldiq»** | Yetishmayotgan summani bitta bosishda to'ldiradi |
| **Kechiktirilgan cheklar** | Mijoz kutib qolsa, chek yopilmay turadi |
| **Chek shu yerda** | Yakunda oyna: chek ko'rinishi + «Chek chiqarish» + «Tugatish» |
| **Yangi mijoz shu yerda** | Sotuvni to'xtatib, mijozlar sahifasiga borish shart emas |
| **Bo'sh qidiruvda ro'yxat** | Mijoz oynasi ochilishi bilan ro'yxat ko'rinadi |

### Egaga / adminga

| Yengillik | Nima beradi |
|-----------|-------------|
| **Excel'dan import** | Butun assortimentni bir marta yuklash (preview bilan) |
| **Buyurtma tavsiyasi** | Nimani qancha olish kerakligi o'zi hisoblanadi |
| **Kam qolgan ogohlantirish** | Tovar tugashidan oldin xabar (Telegram + panel) |
| **Inventarizatsiya** | Farq avtomatik hisoblanadi va jurnalga yoziladi |
| **Chiqimni tasdiqlash** | Kassadan pul faqat ega ruxsati bilan chiqadi |
| **Narx tarixi va audit** | Kim, qachon, nimani o'zgartirgani yozib boriladi |
| **Xodim samaradorligi** | Kim qancha sotdi — bitta hisobotda |
| **Sotuv narxini jadvalda tahrirlash** | Sahifadan chiqmasdan, bitta bosishda |
| **Telegram orqali hisobot** | Panelga kirmasdan: kunlik savdo, qarzdorlar, kam qolgan, faktura |

### Telegram boti (butun platformaga bitta bot)

- Xodim botga yozadi → bot 6 xonali kod beradi → xodim uni **Akkaunt** sahifasiga
  kiritadi → shundan keyin bot uni taniydi
- Tugmalar: **📊 Kunlik savdo · 💰 Qarzdorlar · 📦 Kam qolgan · 🧾 Faktura**
- Har bir tugma xodimning **o'z ruxsatlariga** bo'ysunadi (kassir tannarx/foyda
  ustunlarini olmaydi)
- Avtomatik xabarlar: kunlik xulosa, kam qolgan tovar, obuna eslatmasi
- Chat ID qo'lda kiritilmaydi — u egalikni isbotlamaydi

### Tizim darajasidagi qulayliklar

- **Uch til**: o'zbek · rus · ingliz (butun interfeys va hujjatlar)
- **Toshkent vaqti** hamma joyda izchil (server UTC bo'lsa ham)
- **Real vaqt** (SignalR) — chek o'zgarishi boshqa ekranda darhol ko'rinadi
- **Idempotentlik** — «ikki marta bosildi» pulni ikki marta yozmaydi (7 ta muhim amalda)
- **Sessiyalar ro'yxati** — qayerdan kirilgani ko'rinadi, uzoqdan uzish mumkin
- **Kirish tarixi** va muvaffaqiyatsiz urinishlar bo'yicha bloklash

---

## 5. Muhim texnik yechimlar

### 5.1 Ko'p ijarali izolyatsiya (multi-tenancy)

Do'kon **faqat imzolangan JWT ichidagi `MarketId`** dan aniqlanadi — Host
sarlavhasi yoki URL'dan emas (uni har kim o'zgartira oladi). EF Core global
filtri har bir so'rovga do'kon shartini o'zi qo'shadi, ya'ni servis yozuvchi
uni unutib qololmaydi. Kesib o'tuvchi konsol so'rovlari `IgnoreQueryFilters()`
bilan **ochiq-oydin** belgilanadi.

Testda alohida qatlam bor: `TenantIsolationTests`.

### 5.2 Sub-path + login bog'lanishi

`/{do'kon}/login` sahifasiga faqat **o'sha do'konga biriktirilgan** login kira
oladi. Bitta username ikki do'konda bo'lsa ham, sub-path uni bir qiymatli
qiladi. SuperAdmin esa hech qaysi do'konga tegishli emas — u ildizdagi `/login`
dan kiradi va konsolga o'zi yo'naltiriladi.

### 5.3 Obuna holati mashinasi

```
Active  → muddat ichida
Overdue → muddat o'tdi, lekin grace davrida (hammasi ishlaydi + sariq plashka)
Restricted → grace tugadi: SOTUV YARATISH va ZAKUP QABUL bloklanadi,
             qolgani (ko'rish, hisobot, qarz yig'ish) ishlayveradi
Blocked → to'liq yopiq (402)
```

Grace, eslatma va to'liq blok kunlari platforma sozlamalaridan keladi — kodda
qattiq yozilgan raqam yo'q. To'lov qabul qilinganda yangi muddat **eski
muddatdan** boshlab uzayadi (agar u hali xizmat ko'rsatilayotgan bo'lsa),
aks holda bugundan — mijoz to'lagan kunini yo'qotmaydi.

### 5.4 Pul harakati — bitta yo'l

Barcha to'lovlar (bitta usul ham, miks ham) **bitta metodga** boradi:
tranzaksiya ochiladi, `Sale` qatori `FOR UPDATE` bilan qulflanadi, keyin
holat va qarz bir marta hisoblanadi. Shu sababli:
- ikkita parallel to'lov bir-birini o'chirib yubormaydi;
- to'liq yopadigan miks oraliqda «qarz» holatiga tushmaydi.

### 5.5 Xavfsizlik

- SuperAdmin konsoli **yashirin segment** ostida: noto'g'ri manzil
  autentifikatsiyagacha 404 qaytaradi (skaner konsol borligini ham bilmaydi)
- Ruxsatlar atribut-siyosat orqali (`[RequirePermission]`), ba'zi amallar
  qo'shimcha rol shartiga ega (zakup o'chirish — faqat Owner)
- Parollar BCrypt; SMS ishlatilmaydi — parol unutilsa admin yangisini beradi
- Telegram bog'lanish kodi: 10 daqiqa, bir martalik, chatga bog'langan,
  15 daqiqada 5 urinish chegarasi
- Audit yozuvlari **faqat qo'shiladi** (DB qoidasi `DELETE` ni taqiqlaydi)
- Webhook maxfiy token bilan, tokensiz — yopiq
- TLS standart; sertifikat bo'lmasa vaqtinchalik o'zi imzolagani yaratiladi
  (stack HTTPS'siz ko'tarilmaydi)

### 5.6 Ishonchlilik

| Muammo | Yechim |
|--------|--------|
| Ikki marta bosish | Idempotentlik kaliti + javob keshi |
| Log jadvali cheksiz o'sishi | Sutkalik tozalash (30 kun, sozlanadi) |
| Deploydan keyingi birinchi so'rov | `web` API «healthy» bo'lguncha kutadi |
| Bo'sh chek raqam olib qolishi | Chek birinchi tovar bilan tug'iladi |
| Telegram webhook imkonsiz muhitlar | Long-polling rejimi (ochiq IP kerak emas) |

---

## 6. Sifat holati

| Ko'rsatkich | Qiymat |
|-------------|--------|
| Testlar | **135 / 135** o'tadi |
| Backend build | 0 xato, 0 ogohlantirish |
| Frontend | `tsc` va `eslint` toza |
| Migratsiyalar | 38 ta, startda avtomatik qo'llanadi |
| CI | Backend + frontend + deploy konfiguratsiyasi (nginx/compose validatsiyasi) |

Testlar biznes yadrosini qamraydi: ijara izolyatsiyasi, to'lov va qarz,
sotuvni bekor qilish, ruxsatlar, obuna holati va billing matematikasi,
Telegram bog'lanishi, SuperAdmin oqimlari, PDF render.

---

## 7. Ishga tushirish

**Lokal:**
```bash
# API
cd Buildix.API && dotnet run          # http://localhost:8080

# SPA
cd Buildix.Web && npm run dev         # http://localhost:5173
```

**Ishlab chiqarish:**
```bash
cp .env.example .env    # DB paroli, JWT_KEY (≥32), SUPERADMIN_*
docker compose up -d --build
```
TLS standart yoqiq; haqiqiy sertifikat — `deploy/README.md` §2 (certbot).

---

## 8. Qo'shimcha hujjatlar

| Fayl | Nima haqida |
|------|-------------|
| `docs/TZ-sub-path-login-va-obuna.md` | Sub-path, login bog'lanishi, obuna siyosati |
| `docs/SUPERADMIN-DESIGN-INTEGRATION-TZ.md` | SuperAdmin konsoli: bosqichlar va qarorlar |
| `docs/ADMIN-DESIGN-INTEGRATION-TZ.md` | Admin panel dizayn integratsiyasi |
| `docs/SELLER-INTEGRATION-PLAN.md` | Kassir interfeysi rejasi |
| `docs/BACKEND-GAP-ANALYSIS.md` | Dizayn ↔ backend bo'shliqlari tahlili |
| `docs/FRAUD-AUDIT-HARDENING.md` | Suiiste'molga qarshi choralar |
| `deploy/README.md` | Deploy, TLS, Telegram bot, zaxira nusxa |
