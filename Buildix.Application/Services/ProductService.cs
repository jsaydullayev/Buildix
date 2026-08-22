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
    private readonly IStockLedger _stockLedger;

    public ProductService(IUnitOfWork unitOfWork, IAppDbContext context, ICurrentMarketService currentMarketService, IAuditLogService auditLog, IStockLedger stockLedger)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _currentMarketService = currentMarketService;
        _auditLog = auditLog;
        _stockLedger = stockLedger;
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

        var barcode = NormalizeBarcode(request.Barcode);
        if (barcode is not null && !Barcodes.Symbology.TryNormalize(barcode, out barcode, out var barcodeError))
            return Result.Failure<ProductDto>(barcodeError!, "INVALID_BARCODE");
        if (barcode is not null)
        {
            var barcodeTaken = await _unitOfWork.Products.AnyAsync(
                p => p.MarketId == marketId.Value && p.Barcode == barcode && !p.IsDeleted,
                cancellationToken);
            if (barcodeTaken)
                return Result.Failure<ProductDto>($"'{barcode}' shtrix-kodi boshqa mahsulotga biriktirilgan.");
        }

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
            Sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim(),
            Barcode = barcode,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsHidden = request.IsHidden
        };

        await _unitOfWork.Products.AddAsync(product, cancellationToken);

        // Boshlang'ich qoldiq — ledger' da InitialStock sifatida (product endi
        // Quantity bilan, ResultingQty to'g'ri chiqadi). 0 bo'lsa Record e'tibor
        // bermaydi. Bir SaveChanges'da product + harakat birga yoziladi.
        _stockLedger.Record(product, product.Quantity, StockMovementType.InitialStock, userId: sellerId);

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
        // Quantity odatda zakup/sotuv orqali harakatlanadi — istisno: Owner
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
        // Shtrix-kod: xuddi shu qoida, lekin yozishdan oldin band emasligi
        // tekshiriladi — aks holda Postgres unikal indeksi xom 23505 bilan
        // rad etadi va kassir "nimadir xato" degan xabardan boshqa hech narsa
        // ko'rmaydi. O'zini hisobga olmaymiz: kodni o'zgartirmasdan saqlash
        // ishlashi kerak.
        if (request.Barcode is not null)
        {
            var newBarcode = NormalizeBarcode(request.Barcode);
            if (newBarcode is not null && !Barcodes.Symbology.TryNormalize(newBarcode, out newBarcode, out var barcodeError))
                return Result.Failure<ProductDto>(barcodeError!, "INVALID_BARCODE");
            if (newBarcode is not null && newBarcode != product.Barcode)
            {
                var taken = await _unitOfWork.Products.AnyAsync(
                    p => p.MarketId == marketId && p.Barcode == newBarcode && p.Id != product.Id && !p.IsDeleted,
                    cancellationToken);
                if (taken)
                    return Result.Failure<ProductDto>($"'{newBarcode}' shtrix-kodi boshqa mahsulotga biriktirilgan.");
            }
            product.Barcode = newBarcode;
        }
        // Tavsif: edit-forma egasi; null/bo'sh — tozalash.
        product.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        // Faqat Owner/SuperAdmin (canEditStock) va faqat qiymat kelganda qo'llanadi.
        // Manfiy qiymat DTO Range validatsiyasida allaqachon rad etiladi.
        var oldQuantity = product.Quantity;
        if (canEditStock && request.Quantity.HasValue)
        {
            product.Quantity = request.Quantity.Value;
            // Owner qo'lda tuzatgan qoldiq — Correction harakati (delta = farq).
            if (product.Quantity != oldQuantity)
                _stockLedger.Record(product, product.Quantity - oldQuantity, StockMovementType.Correction,
                    userId: actorUserId, comment: "Qo'lda tuzatish");
        }

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

    /// <summary>
    /// Товары/Склад ekranidagi inline tahrir: sotuv narxi / min. qoldiq /
    /// ko'rinish (Скрыть). Faqat berilgan maydon(lar) o'zgaradi. Narx o'zgarishi
    /// marjaga ta'sir qiladi, ko'rinish esa kassa katalogini o'zgartiradi —
    /// ikkalasi ham auditlanadi (eski→yangi). Qoldiq/tannarxga TEGMAYDI.
    /// </summary>
    public async Task<Result<ProductDto>> PatchProductAsync(Guid id, ProductPatchDto request, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        if (request.SalePrice is null && request.MinThreshold is null && request.IsHidden is null && request.WarehouseLocation is null)
            return Result.Failure<ProductDto>("O'zgartirish uchun kamida bitta maydon yuboring.");

        var marketId = _currentMarketService.GetCurrentMarketId();
        var products = await _unitOfWork.Products.FindAsync(
            p => p.Id == id && p.MarketId == marketId, cancellationToken);
        var product = products.FirstOrDefault();
        if (product is null)
            return Result.Failure<ProductDto>("Mahsulot topilmadi.", "NOT_FOUND");

        var changes = new Dictionary<string, object?>();

        if (request.SalePrice is { } newPrice && newPrice != product.SalePrice)
        {
            changes["salePrice"] = new { from = product.SalePrice, to = newPrice };
            product.SalePrice = newPrice;
        }
        if (request.MinThreshold is { } newMin && newMin != product.MinThreshold)
        {
            changes["minThreshold"] = new { from = product.MinThreshold, to = newMin };
            product.MinThreshold = newMin;
        }
        if (request.IsHidden is { } newHidden && newHidden != product.IsHidden)
        {
            changes["isHidden"] = new { from = product.IsHidden, to = newHidden };
            product.IsHidden = newHidden;
        }
        if (request.WarehouseLocation is not null)
        {
            var loc = string.IsNullOrWhiteSpace(request.WarehouseLocation) ? null : request.WarehouseLocation.Trim();
            if (loc != product.WarehouseLocation)
            {
                changes["warehouseLocation"] = new { from = product.WarehouseLocation, to = loc };
                product.WarehouseLocation = loc;
            }
        }

        // Hech narsa o'zgarmadi — bekorga audit yozmaymiz, joriy holatni qaytaramiz.
        if (changes.Count == 0)
            return Result.Success(ProductMapper.MapToDto(product));

        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Narx marjaga, ko'rinish kassa katalogiga ta'sir qiladi — inline bo'lsa
        // ham iz qoldiramiz (kim, nima, eski→yangi).
        await _auditLog.LogActionAsync(
            AuditEntityTypes.Product, product.Id, AuditActions.Update, actorUserId,
            new { productName = product.Name, changes });

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
            // Inventarizatsiya farqi — Correction harakati (delta = counted − before).
            _stockLedger.Record(product, counted - before, StockMovementType.Correction,
                userId: actorUserId, comment: "Inventarizatsiya");
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

    /// <summary>
    /// Shtrix-kodni saqlashdan oldin tozalaydi: bo'sh — null, aks holda ichki
    /// bo'shliqlar ham olib tashlanadi.
    ///
    /// <para>Ichki bo'shliqlar ataylab: skanerlar ba'zan kodni bo'lib yuboradi
    /// yoki oxiriga bo'shliq qo'shadi, qo'lda kiritishda esa "4 780 123" kabi
    /// guruhlab yozib yuborishadi. Tozalanmasa, bazadagi "4780123456789" bilan
    /// skanerdan kelgan qiymat aynan mos tushmaydi va tovar "topilmadi" bo'lib
    /// qoladi — buni kassir hech qachon tushunmaydi.</para>
    /// </summary>
    private static string? NormalizeBarcode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = new string([.. raw.Where(c => !char.IsWhiteSpace(c))]);
        return cleaned.Length == 0 ? null : cleaned;
    }
}
