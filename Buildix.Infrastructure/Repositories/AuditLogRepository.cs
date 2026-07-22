using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class AuditLogRepository : BaseRepository<AuditLog>
{
    public AuditLogRepository(AppDbContext context) : base(context) { }
}
