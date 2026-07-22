using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class ProductCategoryRepository : BaseRepository<ProductCategory>
{
    public ProductCategoryRepository(AppDbContext context) : base(context) { }
}
