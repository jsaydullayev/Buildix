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
/// decide what comes back. A chat with no match is shown its own id once so the
/// person can paste it into Account; it learns nothing about any shop.</para>
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
    private readonly ILogger<TelegramBotCommandHandler> _logger;

    public TelegramBotCommandHandler(
        AppDbContext db,
        ITelegramNotifier notifier,
        ITashkentClock clock,
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
            // Whatever they sent, answer with the one thing they need: their id.
            await _notifier.SendWithKeyboardAsync(chatId,
                "<b>Buildix</b>\nBu Telegram hisobi hech qanday do'konga bog'lanmagan.\n\n" +
                $"Sizning Telegram ID: <code>{chatId}</code>\n\n" +
                "Buildix panelida <b>Аккаунт → Telegram ID</b> maydoniga shu raqamni saqlang — " +
                "keyin bu yerдan ma'lumot olishingiz mumkin bo'ladi.",
                [], ct);
            return;
        }

        var marketId = user.MarketId!.Value;
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

    private async Task ShowMenuAsync(long chatId, User user, CancellationToken ct)
    {
        var rows = new List<IReadOnlyList<string>>();
        var top = new List<string>();
        if (user.HasPermission(PermissionKeys.SalesAccess)) top.Add(BtnSales);
        if (user.HasPermission(PermissionKeys.DebtsAccess)) top.Add(BtnDebts);
        if (top.Count > 0) rows.Add(top);

        var bottom = new List<string>();
        if (user.HasPermission(PermissionKeys.ProductsAccess)) bottom.Add(BtnStock);
        if (user.HasPermission(PermissionKeys.SalesInvoice)) bottom.Add(BtnInvoice);
        if (bottom.Count > 0) rows.Add(bottom);

        var text = rows.Count == 0
            ? $"<b>Buildix</b>\nSalom, {Escape(user.FullName)}!\n\nSizda hozircha ma'lumot olish ruxsati yo'q."
            : $"<b>Buildix</b>\nSalom, {Escape(user.FullName)}!\n\nKerakli bo'limni tanlang:";

        await _notifier.SendWithKeyboardAsync(chatId, text, rows, ct);
    }

    private async Task DailySalesAsync(long chatId, User user, CancellationToken ct)
    {
        if (!Allowed(chatId, user, PermissionKeys.SalesAccess, out var deny)) { await deny; return; }

        var today = _clock.TodayLocal;
        var (from, to) = _clock.LocalDayToUtcRange(today);

        // A short read-now summary, then the spreadsheet for the details.
        var caption = await _summary.BuildAsync(user.MarketId!.Value, today, ct);
        var file = await _salesExcel.ExportSalesAsync(
            LangOf(user),
            canViewCost: user.HasPermission(PermissionKeys.DataCostPrice),
            canViewProfit: user.HasPermission(PermissionKeys.DataProfit),
            from, to, ct);

        await _notifier.SendDocumentAsync(chatId, file.Content, FileName("savdo"), caption, ct);
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
        if (!Allowed(chatId, user, PermissionKeys.ProductsAccess, out var deny)) { await deny; return; }

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
