using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class SaleItemRepository : BaseRepository<SaleItem>
{
    public SaleItemRepository(AppDbContext context) : base(context) { }
}
