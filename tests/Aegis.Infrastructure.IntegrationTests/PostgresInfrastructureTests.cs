using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.ValueObjects;
using Aegis.Infrastructure.Persistence;
using Aegis.Infrastructure.Services;
using Aegis.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aegis.Infrastructure.IntegrationTests;

[Collection("Postgres")]
public sealed class PostgresInfrastructureTests : IClassFixture<PostgresContainerFixture>
{
    private readonly PostgresContainerFixture _fixture;
    private string Connection => _fixture.ConnectionString;

    public PostgresInfrastructureTests(PostgresContainerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Migration_creates_fk_indexes_and_partial_active_refresh_constraint()
    {
        await using var db = Create();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
        await using var command = new NpgsqlConnection(Connection);
        await command.OpenAsync();
        await using var sql = new NpgsqlCommand("select count(*) from pg_indexes where indexname = 'IX_refresh_tokens_one_active_per_session'", command);
        Assert.Equal(1L, (long)(await sql.ExecuteScalarAsync())!);
        await using var fk = new NpgsqlCommand("select count(*) from pg_constraint where conname = 'FK_sessions_users' or (conrelid = 'sessions'::regclass and confrelid = 'users'::regclass)", command);
        Assert.True((long)(await fk.ExecuteScalarAsync())! >= 1);
        await db.Database.MigrateAsync("0");
        await using var rolledBack = new NpgsqlCommand("select count(*) from information_schema.tables where table_schema = 'public' and table_name in ('users','sessions','refresh_tokens','audit_events')", command);
        Assert.Equal(0L, (long)(await rolledBack.ExecuteScalarAsync())!);
        await db.Database.MigrateAsync();
    }

    [Fact]
    public async Task Concurrent_rotation_allows_one_winner_and_one_reuse()
    {
        await using (var setup = Create()) { await setup.Database.EnsureDeletedAsync(); await setup.Database.MigrateAsync(); }
        var user = new User(Guid.NewGuid(), new Email($"{Guid.NewGuid():N}@example.com"), "hash");
        var now = DateTimeOffset.UtcNow;
        var initial = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"), now, now.AddHours(1));
        var session = new Session(initial.Id == Guid.Empty ? Guid.NewGuid() : initial.SessionId, user.Id, now, now.AddHours(1), [initial]);
        await using (var seed = Create()) { seed.Users.Add(new UserRow { Id = user.Id, Email = user.Email.Value, PasswordHash = user.PasswordHash, Role = (int)user.Role, IsActive = true, Version = 1 }); seed.Sessions.Add(new SessionRow { Id = session.Id, UserId = user.Id, CreatedAt = now, ExpiresAt = now.AddHours(1), Version = 1, RefreshTokens = [new RefreshTokenRow { Id = initial.Id, SessionId = session.Id, Hash = initial.Hash, CreatedAt = now, ExpiresAt = now.AddHours(1) }] }); await seed.SaveChangesAsync(); }
        var a = Rotate(session.Id, initial.Hash, Guid.NewGuid().ToString("N"), now);
        var b = Rotate(session.Id, initial.Hash, Guid.NewGuid().ToString("N"), now);
        var results = await Task.WhenAll(a, b);
        Assert.Single(results, x => x.IsSuccess);
        Assert.Single(results, x => x.Code == Aegis.Domain.Repositories.SessionRotationCode.RefreshTokenReuse);
        await using var verify = Create(); var persisted = await verify.Sessions.Include(x => x.RefreshTokens).SingleAsync(x => x.Id == session.Id); Assert.True(persisted.RevokedAt is not null); Assert.All(persisted.RefreshTokens, x => Assert.NotNull(x.RevokedAt));
    }

    [Fact]
    public async Task Concurrent_rotation_and_family_revoke_share_user_lock()
    {
        await using (var setup = Create()) { await setup.Database.EnsureDeletedAsync(); await setup.Database.MigrateAsync(); }
        var userId = Guid.NewGuid(); var sessionId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var hash = Guid.NewGuid().ToString("N");
        await using (var seed = Create()) { seed.Users.Add(new UserRow { Id = userId, Email = $"{Guid.NewGuid():N}@example.com", PasswordHash = "hash", Role = 0, IsActive = true, Version = 1 }); seed.Sessions.Add(new SessionRow { Id = sessionId, UserId = userId, CreatedAt = now, ExpiresAt = now.AddHours(1), Version = 1, RefreshTokens = [new RefreshTokenRow { Id = Guid.NewGuid(), SessionId = sessionId, Hash = hash, CreatedAt = now, ExpiresAt = now.AddHours(1) }] }); await seed.SaveChangesAsync(); }
        var rotate = Rotate(sessionId, hash, Guid.NewGuid().ToString("N"), now);
        var revoke = Revoke(hash, now);
        var rotateResult = await rotate; var revokeResult = await revoke;
        Assert.True(revokeResult.IsSuccess); Assert.True(rotateResult.Code is Aegis.Domain.Repositories.SessionRotationCode.Succeeded or Aegis.Domain.Repositories.SessionRotationCode.RefreshTokenReuse);
        await using var verify = Create(); var persisted = await verify.Sessions.Include(x => x.RefreshTokens).SingleAsync(x => x.Id == sessionId); Assert.NotNull(persisted.RevokedAt); Assert.All(persisted.RefreshTokens, x => Assert.NotNull(x.RevokedAt));
    }

    [Fact]
    public async Task Rollback_decision_does_not_persist_writes()
    {
        await using (var setup = Create()) { await setup.Database.EnsureDeletedAsync(); await setup.Database.MigrateAsync(); }
        await using (var db = Create())
        {
            var uow = new EfUnitOfWork(db, new TransactionRunner(db));
            await uow.ExecuteInTransactionAsync<int>(async ct => { db.AuditEvents.Add(new AuditEventRow { Action = "rollback_probe", CreatedAt = DateTimeOffset.UtcNow }); await Task.CompletedTask; return new(7, TransactionDecision.Rollback); });
            Assert.Empty(db.ChangeTracker.Entries());
        }
        await using var verify = Create(); Assert.Equal(0, await verify.AuditEvents.CountAsync(x => x.Action == "rollback_probe"));
    }

    [Fact]
    public async Task Duplicate_email_and_user_cas_are_atomic()
    {
        await using (var setup = Create()) { await setup.Database.EnsureDeletedAsync(); await setup.Database.MigrateAsync(); }
        var id = Guid.NewGuid(); var domain = new User(id, new Email($"{Guid.NewGuid():N}@example.com"), "hash");
        await using (var seed = Create()) { seed.Users.Add(new UserRow { Id = id, Email = domain.Email.Value, PasswordHash = domain.PasswordHash, Role = 0, IsActive = true, Version = 1 }); await seed.SaveChangesAsync(); }
        await using var firstDb = Create(); var first = new UserRepository(firstDb, new TransactionRunner(firstDb));
        var duplicate = await first.AddIfNotExistsAtomicallyAsync(new User(Guid.NewGuid(), domain.Email, "hash")); Assert.Equal(Aegis.Domain.Repositories.UserInsertCode.DuplicateEmail, duplicate.Code);
        var current = await first.GetByIdAsync(id); var changed = User.Rehydrate(current!.Id, current.Email, "new-hash", current.Role, current.IsActive, current.Version); var winner = await first.UpdateAtomicallyAsync(changed, 1); Assert.True(winner.IsSuccess);
        await using var secondDb = Create(); var second = new UserRepository(secondDb, new TransactionRunner(secondDb)); var stale = User.Rehydrate(id, domain.Email, "stale", domain.Role, true, 1); var loser = await second.UpdateAtomicallyAsync(stale, 1); Assert.Equal(Aegis.Domain.Repositories.UserUpdateCode.ConcurrencyConflict, loser.Code);
        var concurrentEmail = new Email($"{Guid.NewGuid():N}@example.com");
        var inserts = await Task.WhenAll(Insert(concurrentEmail), Insert(concurrentEmail)); Assert.Single(inserts, x => x.Code == Aegis.Domain.Repositories.UserInsertCode.Succeeded); Assert.Single(inserts, x => x.Code == Aegis.Domain.Repositories.UserInsertCode.DuplicateEmail);
    }

    [Fact]
    public async Task Create_and_revoke_all_serialize_on_same_user_lock()
    {
        await using (var setup = Create()) { await setup.Database.EnsureDeletedAsync(); await setup.Database.MigrateAsync(); }
        var userId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; await using (var seed = Create()) { seed.Users.Add(new UserRow { Id = userId, Email = $"{Guid.NewGuid():N}@example.com", PasswordHash = "hash", Role = 0, IsActive = true, Version = 1 }); await seed.SaveChangesAsync(); }
        var sessionId = Guid.NewGuid(); var refresh = new RefreshToken(Guid.NewGuid(), sessionId, Guid.NewGuid().ToString("N"), now, now.AddHours(1)); var session = new Session(sessionId, userId, now, now.AddHours(1), [refresh]);
        var create = Task.Run(async () => { await using var db = Create(); return await new SessionRepository(db, new TransactionRunner(db)).AddIfUserActiveAtomicallyAsync(session, 1); });
        var revoke = Task.Run(async () => { await using var db = Create(); return await new SessionRepository(db, new TransactionRunner(db)).RevokeAllByUserIdAtomicallyAsync(userId, now, SessionRevocationReason.Manual); });
        await Task.WhenAll(create, revoke); var createResult = await create; var revokeResult = await revoke;
        await using var verify = Create(); var persisted = await verify.Sessions.Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.Id == sessionId); Assert.True(createResult.IsSuccess); Assert.True(revokeResult.IsSuccess); if (persisted?.RevokedAt is not null) Assert.All(persisted.RefreshTokens, x => Assert.NotNull(x.RevokedAt)); else if (persisted is not null) Assert.All(persisted.RefreshTokens, x => Assert.Null(x.RevokedAt));
    }

    private async Task<Aegis.Domain.Repositories.SessionRotationResult> Rotate(Guid id, string hash, string replacementHash, DateTimeOffset now)
    {
        await using var db = Create(); var repo = new SessionRepository(db, new TransactionRunner(db));
        var replacement = new RefreshToken(Guid.NewGuid(), id, replacementHash, now, now.AddMinutes(30));
        return await repo.RotateAndPersistAtomicallyAsync(id, hash, replacement, now, 1);
    }
    private async Task<Aegis.Domain.Repositories.SessionOperationResult> Revoke(string hash, DateTimeOffset now) { await using var db = Create(); return await new SessionRepository(db, new TransactionRunner(db)).RevokeByRefreshTokenHashAtomicallyAsync(hash, now, SessionRevocationReason.Manual); }
    private AegisDbContext Create() => _fixture.CreateDbContext();
    private async Task<Aegis.Domain.Repositories.UserInsertResult> Insert(Email email) { await using var db = Create(); return await new UserRepository(db, new TransactionRunner(db)).AddIfNotExistsAtomicallyAsync(new User(Guid.NewGuid(), email, "hash")); }
}
