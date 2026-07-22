using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class ZakupRepository : BaseRepository<Zakup>
{
    public ZakupRepository(AppDbContext context) : base(context) { }
}
