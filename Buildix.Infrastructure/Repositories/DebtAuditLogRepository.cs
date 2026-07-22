using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class DebtAuditLogRepository : BaseRepository<DebtAuditLog>
{
    public DebtAuditLogRepository(AppDbContext context) : base(context)
    {
    }
}
