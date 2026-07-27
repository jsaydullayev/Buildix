using Buildix.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Buildix.API.BackgroundJobs;

/// <summary>
/// <c>app_logs</c> jadvalini belgilangan muddatdan eski yozuvlardan tozalaydi.
///
/// <para>Serilog PostgreSQL sink'i jadvalni o'zi yaratadi, lekin hech qachon
/// tozalamaydi: diskni to'ldirib, ma'lumotlar bazasini butun tizim bilan birga
/// to'xtatib qo'yishi mumkin bo'lgan yagona cheksiz o'sadigan jadval shu.</para>
///
/// <para>Muddat — <c>Logging:RetentionDays</c> (standart 30). <c>0</c> tozalashni
/// butunlay o'chiradi (masalan, tashqi log yig'uvchi ishlatilganda).</para>
/// </summary>
public class LogRetentionBackgroundService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromHours(24);

    /// <summary>Bitta DELETE'da nechta qator. Katta jadvalda bitta ulkan
    /// tranzaksiya vacuum'ni ushlab, WAL'ni shishirib yuboradi.</summary>
    private const int BatchSize = 5_000;

    /// <summary>Bitta o'tishdagi partiyalar chegarasi — birinchi ishga
    /// tushirishda jadval juda katta bo'lsa ham, tsikl cheksiz aylanmaydi;
    /// qolgani ertangi o'tishda tozalanadi.</summary>
    private const int MaxBatchesPerPass = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<LogRetentionBackgroundService> _logger;

    public LogRetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<LogRetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var days = _config.GetValue<int?>("Logging:RetentionDays") ?? 30;
        if (days <= 0)
        {
            _logger.LogInformation("Log retention disabled (Logging:RetentionDays = {Days})", days);
            return;
        }

        // Migratsiya va startup logini kutamiz.
        try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await PurgeAsync(days, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Bitta o'tishdagi xato tsiklni o'ldirmasin.
                _logger.LogError(ex, "Log retention pass failed");
            }
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private async Task PurgeAsync(int days, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // DIQQAT: `timestamp` — `timestamp without time zone` va sink unga
        // JARAYONNING mahalliy vaqtini yozadi. Shu sababli kesim ham
        // `DateTime.Now` dan olinadi: `UtcNow` bo'lsa, konteyner UTC'dan farqli
        // mintaqada ishlaganda kesim soatlarga siljib ketardi.
        //
        // `Kind` MAJBURIY ravishda `Unspecified` — ustun mintaqasiz.
        var cutoff = DateTime.SpecifyKind(DateTime.Now.AddDays(-days), DateTimeKind.Unspecified);

        // Parametr turi ham QO'LDA ko'rsatiladi. Npgsql `DateTime` ni sukut
        // bo'yicha `timestamptz` deb yozadi va `Kind=Unspecified` ni rad etadi
        // («only UTC is supported» — aynan shu xato jonli sinovda chiqdi).
        // UTC ga o'tkazish ham yaramaydi: `timestamptz` ni mintaqasiz ustun bilan
        // solishtirishda Postgres SESSIYA mintaqasidan foydalanib, kesimni
        // soatlarga siljitib yuborardi.
        var total = 0;
        for (var i = 0; i < MaxBatchesPerPass; i++)
        {
            int deleted;
            try
            {
                // Parametrlar HAR partiyada yangidan yaratiladi: bitta
                // `DbParameter` nusxasi ikkita buyruqqa biriktirilmaydi.
                var cutoffParam = new NpgsqlParameter("cutoff", NpgsqlDbType.Timestamp) { Value = cutoff };
                var limitParam = new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = BatchSize };

                // `ctid IN (… LIMIT n)` — partiyalab o'chirish uchun eng arzon
                // usul: indeks yo'q jadvalda ham bitta seq scan bilan cheklanadi.
                deleted = await db.Database.ExecuteSqlRawAsync(
                    """
                    DELETE FROM app_logs
                    WHERE ctid IN (
                        SELECT ctid FROM app_logs WHERE "timestamp" < @cutoff LIMIT @limit
                    )
                    """,
                    [cutoffParam, limitParam],
                    ct);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
            {
                // Sink jadvalni birinchi Warning darajali logda yaratadi —
                // toza o'rnatishda u hali yo'q bo'lishi normal holat.
                _logger.LogDebug("app_logs does not exist yet — nothing to purge");
                return;
            }

            total += deleted;
            if (deleted < BatchSize) break;
        }

        if (total > 0)
        {
            _logger.LogInformation(
                "Log retention: {Count} app_logs rows older than {Cutoff:yyyy-MM-dd} deleted",
                total, cutoff);
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
