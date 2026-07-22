using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

public interface IMarketService
{
    Task<MarketDto?> CreateMarketAsync(CreateMarketRequest request, CancellationToken cancellationToken = default);
    Task<RegisterMarketResponse> RegisterMarketForOwnerAsync(RegisterMarketRequest request, Guid ownerId, CancellationToken cancellationToken = default);
    Task<List<MarketDto>> GetAllMarketsAsync(CancellationToken cancellationToken = default);
    Task<MarketDto?> GetMarketByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<bool> UpdateMarketAsync(int id, string name, string? description, CancellationToken cancellationToken = default);
    Task<bool> DeleteMarketAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Public, pre-auth market-door state by slug (subdomain). Returns null when
    /// the slug is unknown or the market is soft-deleted (<c>!IsActive</c>) so
    /// the caller can return 404; otherwise reports "blocked" | "expired" |
    /// "active". Exposes no user or business data.
    /// </summary>
    Task<PublicMarketStateDto?> GetPublicStateBySubdomainAsync(string subdomain, CancellationToken cancellationToken = default);
}
