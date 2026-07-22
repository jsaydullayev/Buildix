using Buildix.Application.DTOs;
using Buildix.Application.Interfaces;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Buildix.Application.Services;

public class ProductCategoryService : IProductCategoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAppDbContext _context;
    private readonly ICurrentMarketService _currentMarketService;

    public ProductCategoryService(IUnitOfWork unitOfWork, IAppDbContext context, ICurrentMarketService currentMarketService)
    {
        _unitOfWork = unitOfWork;
        _context = context;
        _currentMarketService = currentMarketService;
    }

    public async Task<IEnumerable<ProductCategoryDto>> GetAllCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var categories = await _context.ProductCategories
            .Include(c => c.Products)
            .Where(c => c.MarketId == marketId)
            .OrderBy(c => c.Name)
            .Select(c => new ProductCategoryDto(
                c.Id,
                c.Name,
                c.Description,
                c.Icon,
                c.IsActive,
                c.Products.Count(p => !p.IsDeleted)
            ))
            .ToListAsync(cancellationToken);

        return categories;
    }

    public async Task<ProductCategoryDto?> GetCategoryByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var category = await _context.ProductCategories
            .Include(c => c.Products)
            .Where(c => c.Id == id && c.MarketId == marketId)
            .Select(c => new ProductCategoryDto(
                c.Id,
                c.Name,
                c.Description,
                c.Icon,
                c.IsActive,
                c.Products.Count(p => !p.IsDeleted)
            ))
            .FirstOrDefaultAsync(cancellationToken);

        return category;
    }

    public async Task<ProductCategoryDto> CreateCategoryAsync(CreateProductCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var category = new ProductCategory
        {
            Name = request.Name,
            Description = request.Description,
            Icon = request.Icon,
            MarketId = marketId,
            IsActive = true
        };

        await _unitOfWork.ProductCategories.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProductCategoryDto(
            category.Id,
            category.Name,
            category.Description,
            category.Icon,
            category.IsActive,
            0
        );
    }

    public async Task<ProductCategoryDto?> UpdateCategoryAsync(UpdateProductCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var category = await _context.ProductCategories
            .Include(c => c.Products)
            .Where(c => c.Id == request.Id && c.MarketId == marketId)
            .FirstOrDefaultAsync(cancellationToken);

        if (category is null)
            return null;

        category.Name = request.Name;
        category.Description = request.Description;
        category.Icon = request.Icon;
        category.IsActive = request.IsActive;

        _unitOfWork.ProductCategories.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProductCategoryDto(
            category.Id,
            category.Name,
            category.Description,
            category.Icon,
            category.IsActive,
            category.Products.Count(p => !p.IsDeleted)
        );
    }

    public async Task<bool> DeleteCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        var marketId = _currentMarketService.GetCurrentMarketId();

        var category = await _context.ProductCategories
            .Where(c => c.Id == id && c.MarketId == marketId)
            .FirstOrDefaultAsync(cancellationToken);

        if (category is null)
            return false;

        // ✅ Check if category has products — tenant-scoped so another market's
        // product (which could carry this CategoryId) can't skew the guard.
        var hasProducts = await _context.Products
            .AnyAsync(p => p.CategoryId == id && p.MarketId == marketId && !p.IsDeleted, cancellationToken);

        if (hasProducts)
        {
            throw new InvalidOperationException(
                "Kategoriyaga mahsulotlar bog'langan. Avval mahsulotlarni boshqa kategoriyaga o'tkazing yoki kategoriyani o'chirmang."
            );
        }

        // Soft delete
        category.IsDeleted = true;
        category.DeletedAt = DateTime.UtcNow;
        _unitOfWork.ProductCategories.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
