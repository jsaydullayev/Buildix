using Buildix.Application.Common;
using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>«Возвраты» — first-class qaytarish hujjatlari (В-##).</summary>
public interface ISaleReturnService
{
    Task<Result<SaleReturnDto>> CreateReturnAsync(CreateReturnDto request, Guid userId, CancellationToken cancellationToken = default);
    Task<PagedResult<SaleReturnDto>> GetReturnsPagedAsync(int page, int size, string? reason = null, string? search = null, CancellationToken cancellationToken = default);
    Task<ReturnsSummaryDto> GetReturnsSummaryAsync(DateTime fromUtc, CancellationToken cancellationToken = default);
}
