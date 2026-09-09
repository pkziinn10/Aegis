using Aegis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Aegis.Api.IntegrationTests;

public sealed class PostgresContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("aegis")
        .WithUsername("aegis")
        .WithPassword("aegis-test")
        .Build();
    private readonly RedisContainer _redis = new RedisBuilder().Build();

    public static PostgresContainerFixture Current { get; private set; } = null!;
    public string ConnectionString => _container.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        Current = this;
        await _container.StartAsync();
        await _redis.StartAsync();
        await ResetDatabaseAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        await using var db = new AegisDbContext(new DbContextOptionsBuilder<AegisDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public AegisDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AegisDbContext>()
        .UseNpgsql(ConnectionString).Options);

    public async Task DisposeAsync() { await _redis.DisposeAsync(); await _container.DisposeAsync(); }
}

[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresContainerFixture>;
