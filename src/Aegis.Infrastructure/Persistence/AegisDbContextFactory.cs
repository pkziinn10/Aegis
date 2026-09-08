using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aegis.Infrastructure.Persistence;

public sealed class AegisDbContextFactory : IDesignTimeDbContextFactory<AegisDbContext>
{
    public AegisDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AegisDbContext>().UseNpgsql("Host=localhost;Database=aegis;Username=aegis;Password=design-time").Options);
}
