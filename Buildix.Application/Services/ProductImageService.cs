using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Constants;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Product image concern extracted from ProductService. See
/// <see cref="IProductImageService"/>. Mapping via <see cref="ProductMapper"/>.
/// </summary>
public class ProductImageService : IProductImageService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IProductImageStorage _imageStorage;
    private readonly IAuditLogService _auditLog;

    public ProductImageService(IUnitOfWork unitOfWork, ICurrentMarketService currentMarketService, IProductImageStorage imageStorage, IAuditLogService auditLog)
    {
        _unitOfWork = unitOfWork;
        _currentMarketService = currentMarketService;
        _imageStorage = imageStorage;
        _auditLog = auditLog;
    }

    public async Task<ProductDto?> SetProductImageAsync(Guid productId, byte[] bytes, string extension, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        // Tenant filtri — boshqa marketning mahsulotini topib bo'lmaydi.
        var products = await _unitOfWork.Products.FindAsync(
            p => p.Id == productId && p.MarketId == marketId,
            cancellationToken);
        var product = products.FirstOrDefault();

        if (product is null)
            return null;

        // Eski rasmni o'chiramiz (yetim fayl qolmasin). Best-effort.
        var oldImageUrl = product.ImageUrl;

        var newUrl = await _imageStorage.SaveAsync(marketId, product.Id, bytes, extension, cancellationToken);
        product.ImageUrl = newUrl;
        product.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // DB muvaffaqiyatli yangilangandan keyingina eski faylni o'chiramiz —
        // saqlash muvaffaqiyatsiz bo'lsa, eski rasm hamon ko'rsatiladi.
        if (!string.IsNullOrEmpty(oldImageUrl) && oldImageUrl != newUrl)
            await _imageStorage.DeleteAsync(oldImageUrl, cancellationToken);

        // Audit — faqat flag, hech qachon rasm baytlari/URL emas. (Controllerdan ko'chirildi.)
        await _auditLog.LogActionAsync(
            AuditEntityTypes.Product, product.Id, AuditActions.ProductImageUpdate, actorUserId,
            new { imageSet = true });

        return ProductMapper.MapToDto(product);
    }

    public async Task<ProductDto?> RemoveProductImageAsync(Guid productId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var products = await _unitOfWork.Products.FindAsync(
            p => p.Id == productId && p.MarketId == marketId,
            cancellationToken);
        var product = products.FirstOrDefault();

        if (product is null)
            return null;

        var oldImageUrl = product.ImageUrl;
        product.ImageUrl = null;
        product.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrEmpty(oldImageUrl))
            await _imageStorage.DeleteAsync(oldImageUrl, cancellationToken);

        // Audit — faqat flag. (Controllerdan ko'chirildi.)
        await _auditLog.LogActionAsync(
            AuditEntityTypes.Product, product.Id, AuditActions.ProductImageUpdate, actorUserId,
            new { imageSet = false });

        return ProductMapper.MapToDto(product);
    }
}
