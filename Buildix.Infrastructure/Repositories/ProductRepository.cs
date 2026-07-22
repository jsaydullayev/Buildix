using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class ProductRepository : BaseRepository<Product>
{
    public ProductRepository(AppDbContext context) : base(context) { }
}
