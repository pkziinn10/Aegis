using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aegis.Api.IntegrationTests;

[Collection("Postgres")]
public sealed class DevelopmentUserSeedTests
{
    [Fact]
    public async Task Seed_is_disabled_by_default()
    {
        using var factory = new IntegrationTestFactory();
        _ = factory.CreateClient();

        await using var db = PostgresContainerFixture.Current.CreateDbContext();
        Assert.Empty(await db.Users.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Enabled_seed_creates_both_roles_and_is_idempotent()
    {
        var settings = TestSettings.With(
            ("DevelopmentSeed:Enabled", "true"),
            ("DevelopmentSeed:UserEmail", "seed-user@example.com"),
            ("DevelopmentSeed:UserPassword", "seed-user-password"),
            ("DevelopmentSeed:AdminEmail", "seed-admin@example.com"),
            ("DevelopmentSeed:AdminPassword", "seed-admin-password"));
        using var factory = new IntegrationTestFactory(settings);
        _ = factory.CreateClient();

        await using (var db = PostgresContainerFixture.Current.CreateDbContext())
        {
            var users = await db.Users.AsNoTracking().ToListAsync();
            Assert.Equal(2, users.Count);
            Assert.Equal(1, users.Count(x => x.Role == (int)Aegis.Domain.Enums.UserRole.User));
            Assert.Equal(1, users.Count(x => x.Role == (int)Aegis.Domain.Enums.UserRole.Admin));
        }

        await DevelopmentUserSeeder.SeedAsync(
            factory.Services,
            factory.Services.GetRequiredService<IConfiguration>(),
            factory.Services.GetRequiredService<IHostEnvironment>());

        await using var finalDb = PostgresContainerFixture.Current.CreateDbContext();
        Assert.Equal(2, await finalDb.Users.CountAsync());
    }
}
