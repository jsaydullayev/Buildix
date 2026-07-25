using Buildix.Application.Interfaces;
using Buildix.Application.Interfaces.Reports;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Enums;
using Buildix.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Buildix.API.Services;

/// <summary>Routes one incoming Telegram message to a bot command.</summary>
public interface ITelegramBotCommandHandler
{
    Task HandleAsync(long chatId, string text, HttpContext http, CancellationToken cancellationToken = default);
}

/// <summary>
/// The bot's command surface. One bot serves every market: the sender's chat id
/// is matched against <see cref="User.TelegramChatId"/>, and that user's market
/// and permissions decide what — if anything — comes back.
///
/// Tenant plumbing: the tenant-scoped services (Excel exports, invoice PDF) read
/// the market from <c>HttpContext.Items["MarketId"]</c>, the same slot
/// TenantResolutionMiddleware fills for authenticated calls. The webhook is
/// anonymous, so this handler fills it from the resolved user — which is why the
/// user lookup must come first and must never be skipped.
/// </summary>
public class TelegramBotCommandHandler : ITelegramBotCommandHandler
{
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
        var parts = text.TrimStart().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "/faktura@BuildixBot 29" → command "faktura", arg "29".
        var command = parts[0].TrimStart('/').Split('@')[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : null;

        // /id works for ANY chat — it is how a user discovers the number to paste
        // into Account. It reveals only the caller's own Telegram id.
        if (command is "id" or "myid")
        {
            await _notifier.SendToChatAsync(chatId,
                $"Sizning Telegram ID: <code>{chatId}</code>\n\n" +
                "Buildix panelida <b>Аккаунт → Telegram ID</b> maydoniga shu raqamni saqlang.", ct);
            return;
        }

        // MarketId != null also filters out SuperAdmin, who belongs to no shop and
        // so has no market data to hand back.
        var user = await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => u.TelegramChatId == chatId && u.IsActive && !u.IsDeleted
                && u.MarketId != null, ct);
        if (user is null)
        {
            await _notifier.SendToChatAsync(chatId,
                "<b>Buildix</b>\nBu Telegram hisobi hech qanday do'konga bog'lanmagan.\n\n" +
                "/id yuboring va olingan raqamni panelda <b>Аккаунт → Telegram ID</b> ga saqlang.", ct);
            return;
        }

        var marketId = user.MarketId!.Value;
        // From here on every tenant-scoped query must run in the user's market.
        http.Items["MarketId"] = marketId;

        switch (command)
        {
            case "start":
            case "help":
            case "yordam":
                await _notifier.SendToChatAsync(chatId, BuildHelp(user), ct);
                return;

            case "savdo":
            case "sales":
                await DailySalesAsync(chatId, user, ct);
                return;

            case "qarz":
            case "debts":
                await DebtorsAsync(chatId, user, ct);
                return;

            case "qoldiq":
            case "stock":
                await LowStockAsync(chatId, user, ct);
                return;

            case "faktura":
            case "invoice":
                await InvoiceAsync(chatId, user, arg, ct);
                return;

            default:
                await _notifier.SendToChatAsync(chatId, BuildHelp(user), ct);
                return;
        }
    }

    // ── Commands ────────────────────────────────────────────────────────────

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

        await _notifier.SendDocumentAsync(chatId, file.Content, FileName("savdo", user), caption, ct);
    }

    private async Task DebtorsAsync(long chatId, User user, CancellationToken ct)
    {
        if (!Allowed(chatId, user, PermissionKeys.DebtsAccess, out var deny)) { await deny; return; }

        var file = await _debtsExcel.ExportDebtsAsync(LangOf(user), ct);
        await _notifier.SendDocumentAsync(chatId, file.Content, FileName("qarzdorlar", user),
            "<b>Qarzdorlar</b>", ct);
    }

    private async Task LowStockAsync(long chatId, User user, CancellationToken ct)
    {
        if (!Allowed(chatId, user, PermissionKeys.ProductsAccess, out var deny)) { await deny; return; }

        var file = await _productsExcel.ExportProductsAsync(
            LangOf(user),
            canViewCost: user.HasPermission(PermissionKeys.DataCostPrice),
            lowStockOnly: true, ct);
        await _notifier.SendDocumentAsync(chatId, file.Content, FileName("kam-qolgan", user),
            "<b>Kam qolgan mahsulotlar</b>", ct);
    }

    private async Task InvoiceAsync(long chatId, User user, string? arg, CancellationToken ct)
    {
        if (!Allowed(chatId, user, PermissionKeys.SalesInvoice, out var deny)) { await deny; return; }

        if (string.IsNullOrWhiteSpace(arg))
        {
            await _notifier.SendToChatAsync(chatId, "Chek raqamini yuboring, masalan: <code>/faktura 29</code>", ct);
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
            await _notifier.SendToChatAsync(chatId, "Chek raqami noto'g'ri. Masalan: <code>/faktura 29</code>", ct);
            return;
        }

        try
        {
            var pdf = await _pdf.GenerateInvoicePdfAsync(saleId, LangOf(user), compact: false);
            await _notifier.SendDocumentAsync(chatId, pdf, $"Faktura_{arg}.pdf", $"<b>Faktura №{arg}</b>", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invoice generation failed for sale {SaleId}", saleId);
            await _notifier.SendToChatAsync(chatId, "Fakturani tayyorlab bo'lmadi.", ct);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Task NotFound(long chatId, string arg, CancellationToken ct) =>
        _notifier.SendToChatAsync(chatId, $"№{arg} chek topilmadi.", ct);

    /// <summary>
    /// Permission gate. Returns false and hands back the "no access" reply task —
    /// the same key the web panel gates on, so revoking a permission silently
    /// removes the command too.
    /// </summary>
    private bool Allowed(long chatId, User user, string permission, out Task deny)
    {
        if (user.HasPermission(permission)) { deny = Task.CompletedTask; return true; }
        deny = _notifier.SendToChatAsync(chatId, "Bu ma'lumotga ruxsatingiz yo'q.", CancellationToken.None);
        return false;
    }

    private static string LangOf(User user) => user.Language.ToCode();

    private string FileName(string kind, User user) =>
        $"{kind}_{_clock.NowLocal:yyyy-MM-dd}.xlsx";

    /// <summary>Only the commands this user may actually run.</summary>
    private static string BuildHelp(User user)
    {
        var lines = new List<string> { "<b>Buildix</b>", $"Salom, {user.FullName}!", "", "<b>Buyruqlar</b>" };
        if (user.HasPermission(PermissionKeys.SalesAccess)) lines.Add("/savdo — bugungi savdo (Excel)");
        if (user.HasPermission(PermissionKeys.DebtsAccess)) lines.Add("/qarz — qarzdorlar (Excel)");
        if (user.HasPermission(PermissionKeys.ProductsAccess)) lines.Add("/qoldiq — kam qolgan mahsulotlar (Excel)");
        if (user.HasPermission(PermissionKeys.SalesInvoice)) lines.Add("/faktura 29 — chek fakturasi (PDF)");
        lines.Add("/id — Telegram ID");
        return string.Join('\n', lines);
    }
}
