using Buildix.Application.Common;
using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Constants;
using Buildix.Domain.Enums;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

public class ProductService : IProductService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;
    private readonly IAuditLogService _auditLog;

    public ProductService(IUnitOfWork unitOfWork, IAppDbContext context, ICurrentMarketService currentMarketService, IAuditLogService auditLog)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _currentMarketService = currentMarketService;
        _auditLog = auditLog;
    }

    public async Task<Result<ProductDto>> CreateProductAsync(CreateProductDto request, Guid? sellerId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.TryGetCurrentMarketId();

        if (!marketId.HasValue)
        {
            throw new UnauthorizedAccessException("Siz hali market yaratmagansiz. Iltimos, avval market yaratiling.");
        }

        var unitValue = request.Unit == 0 ? 1 : request.Unit;
        if (!Enum.IsDefined(typeof(UnitType), unitValue))
        {
            return Result.Failure<ProductDto>("Noto'g'ri o'lchov birligi tanlandi!");
        }

        // Per-market product name uniqueness — surface a friendly error before
        // EF lets Postgres reject the insert with a raw 23505. The DB index is
        // partial on `IsDeleted = false` so a re-created product after delete works.
        var nameTaken = await _unitOfWork.Products.AnyAsync(
            p => p.MarketId == marketId.Value && p.Name == request.Name && !p.IsDeleted,
            cancellationToken);
        if (nameTaken)
            return Result.Failure<ProductDto>($"'{request.Name}' nomli mahsulot allaqachon mavjud.");

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            IsTemporary = request.IsTemporary,
            CreatedBySellerId = sellerId,
            CostPrice = request.CostPrice, // Formadan; 0 bo'lsa keyin zakup orqali
            SalePrice = request.SalePrice,
            MinSalePrice = request.MinSalePrice,
            // Boshlang'ich qoldiq: do'konda bor, lekin zakupsiz tovarlar uchun
            // foydalanuvchi kiritgan miqdor. Keyingi qoldiq o'zgarishlari zakup
            // orqali davom etadi.
            Quantity = request.Quantity,
            MinThreshold = request.MinThreshold,
            Unit = (UnitType)unitValue,  // ✅ NEW: Unit type
            MarketId = marketId.Value,  // Multi-tenancy
            CategoryId = request.CategoryId,  // Category
            HidePriceFromSellers = request.HidePriceFromSellers,
            Sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim()
        };

        await _unitOfWork.Products.AddAsync(product, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ProductMapper.MapToDto(product));
    }

    public async Task<Result<ProductDto>> UpdateProductAsync(UpdateProductDto request, Guid actorUserId, bool canEditStock = false, bool canEditCost = false, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var products = await _unitOfWork.Products.FindAsync(
            p => p.Id == request.Id && p.MarketId == marketId,
            cancellationToken);
        var product = products.FirstOrDefault();

        if (product is null)
            return Result.Failure<ProductDto>("Mahsulot topilmadi.", "NOT_FOUND");

        var unitValue = request.Unit == 0 ? 1 : request.Unit;
        if (!Enum.IsDefined(typeof(UnitType), unitValue))
        {
            return Result.Failure<ProductDto>("Noto'g'ri o'lchov birligi tanlandi!");
        }

        product.Name = request.Name;
        product.IsTemporary = request.IsTemporary;
        // Quantity odatда zakup/sotuv orqali harakatlanadi — istisno: Owner
        // (canEditStock) uni qo'lda tuzatishi mumkin. CostPrice ham endi forma
        // orqali (canEditCost = Owner/Admin) o'zgartirilishi mumkin; aks holda
        // zakup orqali yangilanadi.
        product.SalePrice = request.SalePrice;
        product.MinSalePrice = request.MinSalePrice;
        product.MinThreshold = request.MinThreshold;
        product.Unit = (UnitType)unitValue;  // ✅ NEW: Update unit
        product.CategoryId = request.CategoryId;  // Category
        product.HidePriceFromSellers = request.HidePriceFromSellers;
        // Sku: null — tegilmaydi; bo'sh satr — tozalash; aks holda trim qilib yoziladi.
        if (request.Sku is not null)
            product.Sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim();

        // Faqat Owner/SuperAdmin (canEditStock) va faqat qiymat kelganda qo'llanadi.
        // Manfiy qiymat DTO Range validatsiyasida allaqachon rad etiladi.
        var oldQuantity = product.Quantity;
        if (canEditStock && request.Quantity.HasValue)
            product.Quantity = request.Quantity.Value;

        // Kelgan narx: faqat cost-ko'ruvchi (Owner/Admin) va qiymat kelganda.
        // Null bo'lsa tegilmaydi — masking tufayli 0 kelib eski narxni bosib
        // ketmasligi uchun.
        if (canEditCost && request.CostPrice.HasValue)
            product.CostPrice = request.CostPrice.Value;

        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Manual stock correction is fraud-sensitive — journal it (old→new)
        // whenever the figure actually changed. Only an Owner/SuperAdmin override
        // (canEditStock) with an incoming value can move it, so this stays quiet
        // for ordinary edits. (Moved verbatim out of ProductsController.)
        if (canEditStock && request.Quantity.HasValue && oldQuantity != product.Quantity)
            await _auditLog.LogActionAsync(
                AuditEntityTypes.Product, product.Id, AuditActions.StockAdjust, actorUserId,
                new { from = oldQuantity, to = product.Quantity });

        return Result.Success(ProductMapper.MapToDto(product));
    }

    public async Task<Result<StocktakeResultDto>> StocktakeAsync(StocktakeRequest request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var items = (request.Items ?? new List<StocktakeItem>())
            // So'nggi kelgan qiymat ustun bo'ladi (dublikat productId bo'lsa).
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Last().CountedQty);

        if (items.Count == 0)
            return Result.Failure<StocktakeResultDto>("Kamida bitta mahsulot yuborilishi kerak.");

        var ids = items.Keys.ToList();
        var products = await _context.Products
            .Where(p => p.MarketId == marketId && ids.Contains(p.Id))
            .ToListAsync(cancellationToken);

        var lines = new List<StocktakeLineResult>();
        foreach (var product in products)
        {
            var counted = items[product.Id];
            var before = product.Quantity;
            if (before == counted) continue; // farq yo'q — tegilmaydi

            product.Quantity = counted;
            lines.Add(new StocktakeLineResult(product.Id, product.Name, before, counted, counted - before));
        }

        if (lines.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            foreach (var line in lines)
                await _auditLog.LogActionAsync(
                    AuditEntityTypes.Product, line.ProductId, AuditActions.Stocktake, actorUserId,
                    new { from = line.Before, to = line.Counted, variance = line.Variance });
        }

        return Result.Success(new StocktakeResultDto(lines.Count, lines));
    }

    public async Task<bool> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var products = await _unitOfWork.Products.FindAsync(
            p => p.Id == id && p.MarketId == marketId,
            cancellationToken);
        var product = products.FirstOrDefault();

        if (product is null)
            return false;

        _unitOfWork.Products.Delete(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateStockAsync(Guid id, decimal quantityChange, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var products = await _unitOfWork.Products.FindAsync(
            p => p.Id == id && p.MarketId == marketId,
            cancellationToken);
        var product = products.FirstOrDefault();

        if (product is null)
            return false;

        // Check if new quantity would be negative
        var newQuantity = product.Quantity + quantityChange;
        if (newQuantity < 0)
            throw new InvalidOperationException($"Insufficient stock. Current: {product.Quantity} {product.GetUnitName()}, Requested change: {quantityChange}");

        product.Quantity = newQuantity;
        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

}
