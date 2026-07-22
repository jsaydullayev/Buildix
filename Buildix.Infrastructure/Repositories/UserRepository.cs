using Microsoft.EntityFrameworkCore;
using Buildix.Domain.Entities;
using Buildix.Domain.Interfaces;
using Buildix.Infrastructure.Data;

namespace Buildix.Infrastructure.Repositories;

public class UserRepository : BaseRepository<User>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);
    }

    public async Task<User?> GetActiveUserAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbSet
            .FirstOrDefaultAsync(u => u.Id == id && u.IsActive, cancellationToken);
    }
}
