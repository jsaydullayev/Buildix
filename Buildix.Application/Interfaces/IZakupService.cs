using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

public interface IZakupService
{
    // ── Single-line (legacy / quick re-stock) ────────────────────────────────
    Task<ZakupDto?> GetZakupByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<ZakupDto>> GetAllZakupsAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<ZakupDto>> GetAllZakupsPagedAsync(int page, int size, CancellationToken cancellationToken = default);
    Task<IEnumerable<ZakupDto>> GetZakupsByDateRangeAsync(DateTime start, DateTime end, CancellationToken cancellationToken = default);
    Task<ZakupDto> CreateZakupAsync(CreateZakupDto request, Guid adminId, CancellationToken cancellationToken = default);
    Task<bool> DeleteZakupAsync(Guid id, Guid deletedByUserId, CancellationToken cancellationToken = default);

    // ── Goods-receipt (multi-item + supplier + payment) ──────────────────────
    Task<ZakupReceiptDto> CreateZakupReceiptAsync(CreateZakupReceiptDto request, Guid adminId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ZakupReceiptDto>> GetAllZakupReceiptsAsync(CancellationToken cancellationToken = default);
    Task<PagedResult<ZakupReceiptDto>> GetAllZakupReceiptsPagedAsync(int page, int size, CancellationToken cancellationToken = default);
    Task<ZakupReceiptDto?> GetZakupReceiptByIdAsync(Guid receiptId, CancellationToken cancellationToken = default);
    Task<bool> DeleteZakupReceiptAsync(Guid receiptId, Guid deletedByUserId, CancellationToken cancellationToken = default);
    Task<ZakupReceiptDto?> RegisterSupplierPaymentAsync(Guid receiptId, decimal amount, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Purchase KPI tiles: receipt count and total for goods-receipts created on
    /// or after <paramref name="fromUtc"/> (the caller passes the Tashkent
    /// month-start as a UTC instant). Aggregated DB-side so the page no longer
    /// downloads every receipt just to sum the current month.
    /// </summary>
    Task<PurchaseSummaryDto> GetReceiptsSummaryAsync(DateTime fromUtc, CancellationToken cancellationToken = default);
}
