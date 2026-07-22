using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class PaymentRepository : BaseRepository<Payment>
{
    public PaymentRepository(AppDbContext context) : base(context) { }
}
