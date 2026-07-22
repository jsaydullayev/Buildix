using Buildix.Application.DTOs;

namespace Buildix.Application.Interfaces;

/// <summary>
/// Product image concern extracted from ProductService: set / remove a
/// product's image via <see cref="IProductImageStorage"/>, tenant-scoped.
/// </summary>
public interface IProductImageService
{
    Task<ProductDto?> SetProductImageAsync(Guid productId, byte[] bytes, string extension, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<ProductDto?> RemoveProductImageAsync(Guid productId, Guid actorUserId, CancellationToken cancellationToken = default);
}
