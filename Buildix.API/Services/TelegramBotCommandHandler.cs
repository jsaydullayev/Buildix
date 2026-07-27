using Buildix.Application.Interfaces;
using Buildix.Application.Interfaces.Reports;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Buildix.API.Services;

/// <summary>Routes one incoming Telegram message to a bot action.</summary>
public interface ITelegramBotCommandHandler
{
    Task HandleAsync(long chatId, string text, HttpContext http, CancellationToken cancellationToken = default);
}

/// <summary>
/// The bot's button surface. One bot serves every market.
///
/// <para><b>Identification.</b> The sender's Telegram id comes from the incoming
/// message itself — nobody types it. It is matched against
/// <see cref="User.TelegramChatId"/>, and that user's market and permissions
/// decide what comes back. A chat with no match is handed a one-time link code
/// (<see cref="TelegramLinkCode"/>) to type into Account; it learns nothing about
/// any shop, and because the code only ever reaches THIS chat, redeeming it is
/// what proves the account and the Telegram profile belong to one person.</para>
///
/// <para><b>Buttons, not commands.</b> The reply keyboard carries only the
/// actions this user may run. Telegram sends a tapped button back as ordinary
/// text, so routing matches the caption; the old slash-commands still work as
/// aliases, and a bare receipt number is read as an invoice request.</para>
///
/// <para><b>Tenant plumbing.</b> Tenant-scoped services (Excel exports, invoice
/// PDF) read the market from <c>HttpContext.Items["MarketId"]</c> — the slot
/// TenantResolutionMiddleware fills for authenticated calls. The webhook is
/// anonymous, so this handler fills it from the resolved user, which is why the
/// lookup must come first and must never be skipped.</para>
/// </summary>
public class TelegramBotCommandHandler : ITelegramBotCommandHandler
{
    // Button captions. These are also what Telegram sends back when tapped.
    private const string BtnSales = "📊 Kunlik savdo";
    private const string BtnDebts = "💰 Qarzdorlar";
    private const string BtnStock = "📦 Kam qolgan";
    private const string BtnInvoice = "🧾 Faktura";

    private readonly AppDbContext _db;
    private readonly ITelegramNotifier _notifier;
    private readonly ITashkentClock _clock;
    private readonly ITelegramDailySummaryService _summary;
    private readonly ISalesExcelExportService _salesExcel;
    private readonly IDebtsExcelExportService _debtsExcel;
    private readonly IProductsExcelExportService _productsExcel;
    private readonly IReportPdfExportService _pdf;
    private readonly ITelegramLinkService _link;
    private readonly ILogger<TelegramBotCommandHandler> _logger;

    public TelegramBotCommandHandler(
        AppDbContext db,
        ITelegramNotifier notifier,
        ITashkentClock clock,
        ITelegramLinkService link,
        ITelegramDailySummaryService summary,
        ISalesExcelExportService salesExcel,
        IDebtsExcelExportService debtsExcel,
        IProductsExcelExportService productsExcel,
        IReportPdfExportService pdf,
        ILogger<TelegramBotCommandHandler> logger)
    {
        _db = db;
        _notifier = notifier;
        _clock = clock;
        _link = link;
        _summary = summary;
        _salesExcel = salesExcel;
        _debtsExcel = debtsExcel;
        _productsExcel = productsExcel;
        _pdf = pdf;
        _logger = logger;
    }

    public async Task HandleAsync(long chatId, string text, HttpContext http, CancellationToken ct = default)
    {
        // The id is read from the message metadata — the user never sends it.
        // MarketId != null also filters out SuperAdmin, who belongs to no shop.
        var user = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramChatId == chatId && u.IsActive && !u.IsDeleted
                && u.MarketId != null, ct);
        if (user is null)
        {
            // Whatever they sent, answer with the one thing they need: a code.
            //
            // NOT the raw chat id, which is what this used to send. An id typed
            // into Account proves nothing — anyone could type a stranger's — and
            // since this handler resolves a user BY chat id, that stranger would
            // then be served that shop's receipts, invoices and stock. The code
            // travels the other way: only the owner of this chat ever sees it,
            // so a redeemed code is proof the account and the chat are the same
            // person.
            await IssueLinkCodeAsync(chatId, ct);
            return;
        }

        var marketId = user.MarketId!.Value;

        // The webhook is [AllowAnonymous], so TenantResolutionMiddleware's
        // blocked/expired gates never ran for it. Without this the bot would be
        // a way around a suspension: the panel returns 423/402 while reports keep
        // flowing to Telegram.
        var market = await _db.Markets.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.Id == marketId)
            .Select(m => new { m.IsActive, m.IsBlocked, m.ExpiresAt })
            .FirstOrDefaultAsync(ct);
        if (market is null || !market.IsActive || market.IsBlocked)
        {
            await _notifier.SendWithKeyboardAsync(chatId,
                "Do'kon hisobi vaqtincha to'xtatilgan. Administrator bilan bog'laning.", [], ct);
            return;
        }
        if (market.ExpiresAt is { } expires && expires < _clock.UtcNow)
        {
            await _notifier.SendWithKeyboardAsync(chatId,
                "Do'kon obunasi muddati tugagan. Ma'lumot olish uchun obunani yangilang.", [], ct);
            return;
        }

        // From here on every tenant-scoped query must run in the user's market.
        http.Items["MarketId"] = marketId;

        var raw = text.Trim();
        // "/faktura@BuildixBot 29" → "faktura" + "29"; a tapped button arrives as
        // its plain caption and falls through to the caption comparisons below.
        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var head = parts[0].TrimStart('/').Split('@')[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : null;

        // Sales
        if (raw == BtnSales || head is "savdo" or "sales")
        {
            await DailySalesAsync(chatId, user, ct);
            return;
        }
        // Debtors
        if (raw == BtnDebts || head is "qarz" or "debts")
        {
            await DebtorsAsync(chatId, user, ct);
            return;
        }
        // Low stock
        if (raw == BtnStock || head is "qoldiq" or "stock")
        {
            await LowStockAsync(chatId, user, ct);
            return;
        }
        // Invoice: the button only asks for the number, the number does the work.
        if (raw == BtnInvoice)
        {
            if (!Allowed(chatId, user, PermissionKeys.SalesInvoice, out var denyInv)) { await denyInv; return; }
            await _notifier.SendToChatAsync(chatId,
                "Chek raqamini yuboring — masalan <code>29</code>.", ct);
            return;
        }
        if (head is "faktura" or "invoice")
        {
            await InvoiceAsync(chatId, user, arg, ct);
            return;
        }
        // A bare number after tapping «Faktura» (or at any time) is a receipt no.
        if (IsReceiptNumber(raw))
        {
            await InvoiceAsync(chatId, user, raw, ct);
            return;
        }

        // /start, /help or anything unrecognised — (re)show the keyboard.
        await ShowMenuAsync(chatId, user, ct);
    }

    // ── Actions ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Hands an unlinked chat its one-time code. Re-asking inside the validity
    /// window returns the SAME code, so a person who wrote twice doesn't end up
    /// typing the older of two live codes and being told it is wrong.
    /// </summary>
    private async Task IssueLinkCodeAsync(long chatId, CancellationToken ct)
    {
        try
        {
            var (code, expiresUtc) = await _link.IssueCodeAsync(chatId, ct);
            var minutes = Math.Max(1, (int)Math.Ceiling((expiresUtc - _clock.UtcNow).TotalMinutes));
            await _notifier.SendWithKeyboardAsync(chatId,
                "<b>Buildix</b>\nBu Telegram hisobi hech qanday do'konga bog'lanmagan.\n\n" +
                $"Bog'lanish kodi: <code>{code}</code>\n" +
                $"Kod <b>{minutes} daqiqa</b> amal qiladi.\n\n" +
                "Buildix panelida <b>Аккаунт → Telegram</b> bo'limiga shu kodni kiriting — " +
                "shundan keyin bu yerdan ma'lumot olishingiz mumkin bo'ladi.",
                [], ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telegram link code issue failed for chat {ChatId}", chatId);
            await _notifier.SendToChatAsync(chatId,
                "Kod berishda xatolik. Birozdan keyin qayta yozing.", ct);
        }
    }

    private async Task ShowMenuAsync(long chatId, User user, CancellationToken ct)
    {
        // Keys match the ones each action actually enforces below, so a button is
        // never shown that would answer «ruxsatingiz yo'q».
        var rows = new List<IReadOnlyList<string>>();
        var top = new List<string>();
        if (user.HasPermission(PermissionKeys.SalesExport)) top.Add(BtnSales);
        if (user.HasPermission(PermissionKeys.DebtsAccess)) top.Add(BtnDebts);
        if (top.Count > 0) rows.Add(top);

        var bottom = new List<string>();
        if (user.HasPermission(PermissionKeys.ProductsExport)) bottom.Add(BtnStock);
        if (user.HasPermission(PermissionKeys.SalesInvoice)) bottom.Add(BtnInvoice);
        if (bottom.Count > 0) rows.Add(bottom);

        var text = rows.Count == 0
            ? $"<b>Buildix</b>\nSalom, {Escape(user.FullName)}!\n\nSizda hozircha ma'lumot olish ruxsati yo'q."
            : $"<b>Buildix</b>\nSalom, {Escape(user.FullName)}!\n\nKerakli bo'limni tanlang:";

        await _notifier.SendWithKeyboardAsync(chatId, text, rows, ct);
    }

    private async Task DailySalesAsync(long chatId, User user, CancellationToken ct)
    {
        // The workbook is an export, so it takes the same key the web export
        // endpoint takes (sales.export) — revoking it there must not leave a
        // second door open here.
        if (!Allowed(chatId, user, PermissionKeys.SalesExport, out var deny)) { await deny; return; }

        var today = _clock.TodayLocal;
        var (from, to) = _clock.LocalDayToUtcRange(today);

        // Without data.allSalesView a cashier sees only their OWN receipts —
        // the same server-side scoping GetAllSales applies to the web list.
        // Every figure below is filtered by it, so the bot can't become the
        // way to read the shop's takings.
        var ownScope = user.HasPermission(PermissionKeys.DataAllSalesView) ? (Guid?)null : user.Id;

        // The summary goes as its OWN message, not as the file caption: a caption
        // is capped at 1024 characters and this text (stock list + top products)
        // can exceed that, which would cost the reader the tail of the report.
        // A message allows 4096, so it arrives whole.
        var summary = await _summary.BuildAsync(user.MarketId!.Value, today, new DailySummaryOptions(
            SellerId: ownScope,
            IncludeProfit: user.HasPermission(PermissionKeys.DataProfit),
            IncludeCash: user.HasPermission(PermissionKeys.DataCashBalance),
            IncludeDebts: user.HasPermission(PermissionKeys.DebtsAccess),
            IncludeStock: user.HasPermission(PermissionKeys.ProductsAccess)), ct);
        if (summary is not null)
            await _notifier.SendToChatAsync(chatId, summary, ct);

        var file = await _salesExcel.ExportSalesAsync(
            LangOf(user),
            canViewCost: user.HasPermission(PermissionKeys.DataCostPrice),
            canViewProfit: user.HasPermission(PermissionKeys.DataProfit),
            from, to, ownScope, ct);

        await _notifier.SendDocumentAsync(chatId, file.Content, FileName("savdo"),
            $"<b>Kunlik savdo · {today:dd.MM.yyyy}</b>", ct);
    }

    private async Task DebtorsAsync(long chatId, User user, CancellationToken ct)
    {
        if (!Allowed(chatId, user, PermissionKeys.DebtsAccess, out var deny)) { await deny; return; }

        var file = await _debtsExcel.ExportDebtsAsync(LangOf(user), ct);
        await _notifier.SendDocumentAsync(chatId, file.Content, FileName("qarzdorlar"),
            "<b>Qarzdorlar</b>", ct);
    }

    private async Task LowStockAsync(long chatId, User user, CancellationToken ct)
    {
        // Same key as the web products export.
        if (!Allowed(chatId, user, PermissionKeys.ProductsExport, out var deny)) { await deny; return; }

        var file = await _productsExcel.ExportProductsAsync(
            LangOf(user),
            canViewCost: user.HasPermission(PermissionKeys.DataCostPrice),
            lowStockOnly: true, ct);
        await _notifier.SendDocumentAsync(chatId, file.Content, FileName("kam-qolgan"),
            "<b>Kam qolgan mahsulotlar</b>", ct);
    }

    private async Task InvoiceAsync(long chatId, User user, string? arg, CancellationToken ct)
    {
        if (!Allowed(chatId, user, PermissionKeys.SalesInvoice, out var deny)) { await deny; return; }

        if (string.IsNullOrWhiteSpace(arg))
        {
            await _notifier.SendToChatAsync(chatId, "Chek raqamini yuboring — masalan <code>29</code>.", ct);
            return;
        }

        // Owners type the receipt number they see on screen ("№29"); a full GUID
        // is accepted too so a deep-linked id also works.
        Guid saleId;
        if (Guid.TryParse(arg, out var byId))
        {
            var exists = await _db.Sales.IgnoreQueryFilters()
                .AnyAsync(s => s.Id == byId && s.MarketId == user.MarketId!.Value && !s.IsDeleted, ct);
            if (!exists) { await NotFound(chatId, arg, ct); return; }
            saleId = byId;
        }
        else if (int.TryParse(arg.TrimStart('№', '#', 'N', 'n'), out var number))
        {
            var found = await _db.Sales.IgnoreQueryFilters()
                .Where(s => s.MarketId == user.MarketId!.Value && s.SaleNumber == number && !s.IsDeleted)
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => (Guid?)s.Id)
                .FirstOrDefaultAsync(ct);
            if (found is null) { await NotFound(chatId, arg, ct); return; }
            saleId = found.Value;
        }
        else
        {
            await _notifier.SendToChatAsync(chatId, "Chek raqami noto'g'ri. Masalan: <code>29</code>", ct);
            return;
        }

        try
        {
            var pdf = await _pdf.GenerateInvoicePdfAsync(saleId, LangOf(user), compact: false);
            await _notifier.SendDocumentAsync(chatId, pdf, $"Faktura_{arg}.pdf", $"<b>Faktura №{Escape(arg)}</b>", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invoice generation failed for sale {SaleId}", saleId);
            await _notifier.SendToChatAsync(chatId, "Fakturani tayyorlab bo'lmadi.", ct);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>A short digit-only string — «29», «№29». Long digit runs are left
    /// alone so a pasted Telegram id isn't mistaken for a receipt.</summary>
    private static bool IsReceiptNumber(string s)
    {
        var t = s.TrimStart('№', '#', 'N', 'n').Trim();
        return t.Length is > 0 and <= 9 && t.All(char.IsAsciiDigit);
    }

    private Task NotFound(long chatId, string arg, CancellationToken ct) =>
        _notifier.SendToChatAsync(chatId, $"№{Escape(arg)} chek topilmadi.", ct);

    /// <summary>
    /// Permission gate. Returns false and hands back the "no access" reply task —
    /// the same key the web panel gates on, so revoking a permission silently
    /// removes the button too.
    /// </summary>
    private bool Allowed(long chatId, User user, string permission, out Task deny)
    {
        if (user.HasPermission(permission)) { deny = Task.CompletedTask; return true; }
        deny = _notifier.SendToChatAsync(chatId, "Bu ma'lumotga ruxsatingiz yo'q.", CancellationToken.None);
        return false;
    }

    private static string LangOf(User user) => user.Language.ToCode();

    private string FileName(string kind) => $"{kind}_{_clock.NowLocal:yyyy-MM-dd}.xlsx";

    private static string Escape(string? s) =>
        (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
