using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Buildix.Infrastructure.Data;

/// <summary>
/// Design-time factory used by `dotnet ef` for scaffolding migrations.
/// Reads ConnectionStrings__DefaultConnection from env, falling back to a
/// localhost stub when only the schema is being generated.
/// </summary>
public class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=2025;Database=BuildixDB;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(conn, npg => npg.MigrationsAssembly("Buildix.Infrastructure"))
            .Options;

        return new AppDbContext(options);
    }
}
