using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class ShiftRepository : BaseRepository<Shift>
{
    public ShiftRepository(AppDbContext context) : base(context)
    {
    }
}
