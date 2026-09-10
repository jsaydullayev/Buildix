using Buildix.Domain.Common;
using Buildix.Domain.Enums;

namespace Buildix.Domain.Entities;

/// <summary>
/// Per-market configuration backing the Настройки (Settings) screen. One row
/// per <see cref="Market"/> (1:1, shares the market's int key). Every business
/// toggle/limit the design exposes lives here so the rules can be enforced in
/// the sales/shift/debt flows instead of only being cosmetic UI state.
///
/// Defaults mirror the values shown in the Settings mockup so a freshly
/// provisioned market behaves sensibly before the owner ever opens the screen.
/// </summary>
public class MarketSettings : IUpdateTracked
{
    /// <summary>PK == FK to Market.Id (1:1).</summary>
    public int MarketId { get; set; }
    public Market? Market { get; set; }

    // ── Магазин (store profile) ──────────────────────────────────────────
    public string? Phone { get; set; }
    public string? Address { get; set; }
    /// <summary>Free-form working hours, e.g. "08:00 — 20:00".</summary>
    public string? WorkingHours { get; set; }

    // ── Касса и смены (cash & shift rules) ───────────────────────────────
    /// <summary>Kassir smena ochmasdan sota olmaydi.</summary>
    public bool SalesOnlyWhenShiftOpen { get; set; } = true;
    /// <summary>Naqd yechish egasi tasdig'ini talab qiladi (approval oqimi).</summary>
    public bool CashWithdrawalNeedsApproval { get; set; } = true;
    /// <summary>Qarzga sotish faqat "postoyanniy" mijozlarga.</summary>
    public bool DebtOnlyForRegulars { get; set; } = true;

    /// <summary>
    /// Qarz amallari uchun BULUT bilan aloqa talab qilinadimi.
    /// </summary>
    /// <remarks>
    /// <para><b>Nima uchun.</b> Ikkita kassa o'z bazasi bilan ishlaganda
    /// ular bir-birining qarz yozuvlarini ko'rmaydi. Ikkalasi ham oflayn
    /// holda bitta mijozga qarz bera oladi (chegara ikki marta sarflanadi),
    /// bitta qarzni ikki marta undira oladi, yoki bitta avansni ikki marta
    /// sarflay oladi. Bularning hech biri xato bermaydi — raqamlar keyin
    /// birlashganda to'g'ri kelmay qoladi.</para>
    ///
    /// <para><b>Nima qiladi.</b> Yoqilgan bo'lsa, qarz yaratadigan va qarzni
    /// undiradigan amallar do'konning ma'lumoti YANGI ekanini talab qiladi.
    /// Aloqa yo'q yoki ma'lumot eskirgan bo'lsa, amal rad etiladi va kassir
    /// buni darhol ko'radi — jimgina noto'g'ri yozuvdan ko'ra yaxshiroq.
    /// Naqd va karta savdosi TEGILMAYDI: ular oflayn ham xavfsiz, chunki
    /// pul o'sha yerda va o'sha zahoti olinadi.</para>
    ///
    /// <para><b>Sukut bo'yicha O'CHIQ.</b> Bitta kassali do'konda (va bitta
    /// bazaga ulangan ikki kassada) bunday xavf umuman yo'q: qarz yozuvi
    /// bitta joyda va u har doim o'ziga o'zi mos. Qoidani yoqish faqat
    /// mustaqil bazali kassalar paydo bo'lganda ma'noga ega.</para>
    /// </remarks>
    public bool DebtRequiresCloud { get; set; } = false;
    /// <summary>Bitta mijozga standart qarz limiti (sum). 0 = limitsiz.</summary>
    public decimal DefaultDebtLimit { get; set; } = 15_000_000m;
    /// <summary>Kassada ruxsat etilgan maksimal расхождение (sum).</summary>
    public decimal AllowedCashDiscrepancy { get; set; } = 0m;
    /// <summary>Smena avto-yopilish vaqti (HH:mm), null = avto-yopish yo'q.</summary>
    public TimeOnly? ShiftAutoCloseTime { get; set; }

    // ── Посещаемость (davomat hisobi — §2.15 Смены) ─────────────────────────
    // Do'kon ish grafigi: reja soati = End − Start; smena shu vaqtdan keyin
    // ochilsa "kechikish". Standart 08:00–20:00 · 08:15 — mavjud xatti-harakat.
    /// <summary>Ish kuni boshlanishi (davomat rejasi).</summary>
    public TimeOnly WorkDayStart { get; set; } = new(8, 0);
    /// <summary>Ish kuni tugashi (davomat rejasi).</summary>
    public TimeOnly WorkDayEnd { get; set; } = new(20, 0);
    /// <summary>Kechikish chegarasi — shundan keyin ochilsa "опоздание".</summary>
    public TimeOnly LateThreshold { get; set; } = new(8, 15);

    // ── Чек (receipt) ────────────────────────────────────────────────────
    public string? ReceiptHeader { get; set; }
    public string? ReceiptFooter { get; set; }
    public bool AutoPrintReceipt { get; set; } = false;

    /// <summary>
    /// Chek rulonining eni, millimetrda. Faqat ikki standart qiymat: 58 yoki 80.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega sozlamada.</b> Ilgari eni interfeysga QATTIQ 80 deb
    /// yozilgan edi. 58 mm printer o'rnatilgan do'konda chek qog'ozga
    /// sig'masdi va drayver uni o'zicha siqib bosardi — har bir harf alohida
    /// qatorga tushib, chek yarim metrga cho'zilardi. Sozlash imkoni yo'q
    /// edi.</para>
    ///
    /// <para>Ikki qiymat bilan cheklangan: bular termal rulonlarning
    /// standart o'lchamlari va boshqasini kiritish faqat xato bo'ladi.</para>
    /// </remarks>
    public int ReceiptWidthMm { get; set; } = 80;

    // ── Этикетка (yorliq rulonlari) ──────────────────────────────────────
    // Yorliq printeriga tovar etiketkasi bosiladi. Rulon o'lchami do'kondan
    // do'konga (va hatto bir do'kon ichida) o'zgaradi, shuning uchun u
    // sozlamada turadi.
    //
    // Nega chek eni kabi ikki qiymat bilan cheklanmagan: chek rulonlari
    // amalda faqat 58 va 80 mm bo'ladi, yorliq rulonlari esa yo'q — 57×38,
    // 57×30, 58×40, 40×30, 30×20 va boshqalar bemalol uchraydi. Ro'yxatga
    // olib bo'lmaydi, shuning uchun oraliq beriladi.

    /// <summary>Yorliq eni, mm. Printer boshining eniga sig'ishi kerak.</summary>
    public decimal LabelWidthMm { get; set; } = 58m;

    /// <summary>Yorliq bo'yi, mm.</summary>
    public decimal LabelHeightMm { get; set; } = 40m;

    /// <summary>
    /// Ikki yorliq orasidagi tirqish (gap), mm. Printer yorliq chetini shu
    /// tirqish bo'yicha topadi.
    /// </summary>
    /// <remarks>
    /// Deyarli hamma rulonda 2 mm. Noto'g'ri berilsa printer yorliq
    /// uzunligini xato o'lchaydi va maket qo'shni yorliqqa siljib tushadi.
    /// </remarks>
    public decimal LabelGapMm { get; set; } = 2m;

    /// <summary>
    /// Vertikal siljish tuzatmasi, mm. Maket yorliqqa tepadan pastga
    /// shuncha suriladi.
    /// </summary>
    /// <remarks>
    /// <para><b>Nega kerak.</b> Bir xil o'lchamdagi rulonlar ham qog'ozga
    /// bir xil yopishtirilmaydi va printerning sensori ham modeldan modelga
    /// biroz farq qiladi. Natijada to'g'ri o'lcham berilgan bo'lsa ham maket
    /// bir-ikki millimetrga surilib chiqishi mumkin — tepasi kesiladi.
    /// Ehtimolliklarni kodda taxmin qilib bo'lmaydi, shuning uchun tuzatma
    /// sozlamada: texnik bir marta o'lchab kiritadi.</para>
    ///
    /// <para>MANFIY QIYMAT YO'Q. TSPL da <c>SHIFT</c> diapazoni 0…1016 va
    /// manfiy son yuborilganda firmware uni ishorasiz deb o'qib, maketni bir
    /// necha santimetrga sakratib yuboradi — bitta yorliq o'rniga ikkitasiga
    /// bo'linib tushadi. Buni haqiqiy printerda ko'rdik.</para>
    /// </remarks>
    public decimal LabelOffsetMm { get; set; } = 0m;

    // ── Локаль (locale) ──────────────────────────────────────────────────
    public Language DefaultLanguage { get; set; } = Language.Russian;
    /// <summary>Hafta boshi: 1 = Dushanba (ISO), 7 = Yakshanba.</summary>
    public int FirstDayOfWeek { get; set; } = 1;

    // ── Склад и цены (warehouse & pricing) ───────────────────────────────
    public bool MinStockAlertEnabled { get; set; } = true;
    /// <summary>Sotuv narxi tannarxdan past bo'lsa bloklanadi.</summary>
    public bool BlockSaleBelowCost { get; set; } = true;
    /// <summary>Yangi mahsulot uchun standart ustama (%).</summary>
    public decimal DefaultMarkupPct { get; set; } = 18m;

    // ── Уведомления (Telegram notifications) ─────────────────────────────
    public bool NotifyDaySummary { get; set; } = true;
    public bool NotifyOverdueDebts { get; set; } = true;
    public bool NotifyWithdrawalRequests { get; set; } = true;
    // Telegram bog'lash MarketSettings'dan User.TelegramChatId'ga ko'chirildi:
    // bot endi har bir XODIMNI o'z ID si bo'yicha taniydi (faqat egasini emas),
    // shuning uchun bu yerda market darajasidagi @username/chat_id saqlanmaydi.
    // Bu blokda faqat market darajasidagi Notify* kalitlari qoladi.
    /// <summary>
    /// Kunlik xulosa oxirgi yuborilgan Toshkent kunining UTC boshlanishi.
    /// (Sana emas, UTC instant — ustun `timestamptz`, Npgsql Kind=Unspecified
    /// qiymatni qabul qilmaydi.) Fon vazifasi shu bilan kuniga bir marta
    /// yuborishni kafolatlaydi — qayta ishga tushirish ham takror yubormaydi.
    /// </summary>
    public DateTime? LastDaySummarySentOn { get; set; }

    // ── Безопасность (security) ──────────────────────────────────────────
    /// <summary>Harakatsizlikda avto-chiqish (daqiqa). 0 = o'chirilgan.</summary>
    public int InactivityLogoutMinutes { get; set; } = 0;
    public bool AuditEnabled { get; set; } = true;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
