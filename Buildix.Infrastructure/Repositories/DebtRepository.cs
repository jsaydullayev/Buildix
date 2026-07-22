using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class DebtRepository : BaseRepository<Debt>
{
    public DebtRepository(AppDbContext context) : base(context) { }
}
