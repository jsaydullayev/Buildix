using Buildix.Domain.Entities;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class CustomerRepository : BaseRepository<Customer>
{
    public CustomerRepository(AppDbContext context) : base(context) { }
}
