using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Application.Services.Barcodes;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

/// <summary>
/// Tovar yorliqlari: ichki EAN-13 kod berish va chop etish uchun PDF.
/// </summary>
public class ProductLabelService : IProductLabelService
{
    /// <summary>
    /// Kod tasodifiy yaratiladi va yagonalikni baza indeksi kafolatlaydi, ya'ni
    /// to'qnashuv bo'lsa saqlash yiqiladi. 10 milliard variant ichida bu deyarli
    /// bo'lmaydigan hodisa, lekin "deyarli" yetarli emas — shuning uchun bir
    /// necha marta qayta urinamiz.
    /// </summary>
    private const int GenerateAttempts = 8;

    private readonly IAppDbContext _context;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentMarketService _currentMarketService;

    public ProductLabelService(
        IAppDbContext context,
        IUnitOfWork unitOfWork,
        ICurrentMarketService currentMarketService)
    {
        _context = context;
        _unitOfWork = unitOfWork;
        _currentMarketService = currentMarketService;
    }

    public async Task<Result<string>> GenerateBarcodeAsync(
        Guid productId, bool replaceExisting = false, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var product = await _context.Products
            .FirstOrDefaultAsync(p => p.Id == productId && p.MarketId == marketId, cancellationToken);
        if (product is null)
            return Result.Failure<string>("Mahsulot topilmadi.", "NOT_FOUND");

        // Kod bor va almashtirish so'ralmagan — mavjudini qaytaramiz. Bu ataylab:
        // kod almashsa, allaqachon chop etilgan va tovarlarga yopishtirilgan
        // yorliqlar ishlamay qoladi.
        if (!string.IsNullOrWhiteSpace(product.Barcode) && !replaceExisting)
            return Result.Success(product.Barcode!);

        var code = await AssignUniqueBarcodeAsync(product, marketId, cancellationToken);
        if (code is null)
            return Result.Failure<string>("Bo'sh shtrix-kod topilmadi. Qayta urinib ko'ring.");

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(code);
    }

    public async Task<Result<byte[]>> RenderLabelsAsync(
        PrintLabelsDto request, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var ids = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => p.MarketId == marketId && ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var missing = ids.Count - products.Count;
        if (missing > 0)
            return Result.Failure<byte[]>($"{missing} ta mahsulot topilmadi.", "NOT_FOUND");

        // Kodsiz tovarlarga shu yerda kod beriladi. Aks holda kassir yoki
        // omborchi "chop etish" bosib, "kod yo'q" degan xatoni olardi va uni
        // qayerdan yaratishni o'zi topishi kerak bo'lardi.
        var generated = false;
        foreach (var product in products.Where(p => string.IsNullOrWhiteSpace(p.Barcode)))
        {
            if (await AssignUniqueBarcodeAsync(product, marketId, cancellationToken) is null)
                return Result.Failure<byte[]>($"'{product.Name}' uchun shtrix-kod yaratib bo'lmadi.");
            generated = true;
        }
        if (generated)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        var byId = products.ToDictionary(p => p.Id);
        // So'rovdagi tartib saqlanadi: omborchi ro'yxatda ko'rgan tartibda
        // yorliq chiqishini kutadi.
        var labels = request.Items
            .Select(i => new LabelData(byId[i.ProductId].Name, byId[i.ProductId].Barcode!, byId[i.ProductId].Sku, i.Copies))
            .ToList();

        return Result.Success(LabelPdfRenderer.Render(labels, request.WidthMm, request.HeightMm));
    }

    /// <summary>
    /// Bo'sh ichki kod topib, tovarga yozadi (saqlamaydi — chaqiruvchi bitta
    /// SaveChanges bilan yozadi). Topilmasa null.
    /// </summary>
    private async Task<string?> AssignUniqueBarcodeAsync(
        Product product, int marketId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < GenerateAttempts; attempt++)
        {
            var code = Ean13.NewInternal();
            var taken = await _context.Products
                .AnyAsync(p => p.MarketId == marketId && p.Barcode == code && p.Id != product.Id, cancellationToken);
            if (taken) continue;

            product.Barcode = code;
            return code;
        }
        return null;
    }
}
