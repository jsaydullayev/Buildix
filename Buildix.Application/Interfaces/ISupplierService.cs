using Buildix.Application.Common;
using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

public interface ISupplierService
{
    // Read methods take the caller's role: the confidential OutstandingDebt is
    // zeroed for Sellers inside the service (the redaction used to live in the
    // controller). Pass null for callers that are allowed to see the full figure.
    Task<SupplierDto?> GetSupplierByIdAsync(Guid id, string? userRole, CancellationToken cancellationToken = default);
    Task<IEnumerable<SupplierDto>> GetAllSuppliersAsync(string? userRole, CancellationToken cancellationToken = default);
    Task<PagedResult<SupplierDto>> GetAllSuppliersPagedAsync(int page, int size, string? search, string? userRole, CancellationToken cancellationToken = default);
    Task<Result<SupplierDto>> CreateSupplierAsync(CreateSupplierDto request, CancellationToken cancellationToken = default);
    Task<Result<SupplierDto>> UpdateSupplierAsync(UpdateSupplierDto request, string? userRole, CancellationToken cancellationToken = default);
    Task<bool> SoftDeleteSupplierAsync(Guid id, CancellationToken cancellationToken = default);
    Task<SupplierDeleteInfoDto> GetSupplierDeleteInfoAsync(Guid id, string? userRole, CancellationToken cancellationToken = default);
}
