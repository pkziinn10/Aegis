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
    public async Task Revocation_constraints_reject_invalid_direct_writes_and_allow_valid_states()
    {
        await AssertCheckConstraintViolation(
            db =>
            {
                db.Sessions.Add(NewSession(revokedAt: DateTimeOffset.UtcNow, reason: null));
                return Task.CompletedTask;
            },
            "CK_sessions_revocation_pair",
            "A session with RevokedAt set must also have RevocationReason set");

        await AssertCheckConstraintViolation(
            db =>
            {
                db.Sessions.Add(NewSession(revokedAt: DateTimeOffset.UtcNow, reason: 4));
                return Task.CompletedTask;
            },
            "CK_sessions_revocation_reason_range",
            "A session revocation reason outside the supported range must be rejected");

        await AssertDeferredConstraintViolation(
            db =>
            {
                var session = NewSession(revokedAt: DateTimeOffset.UtcNow, reason: (int)SessionRevocationReason.Manual);
                session.RefreshTokens.Add(new RefreshTokenRow
                {
                    Id = Guid.NewGuid(), SessionId = session.Id, Hash = Guid.NewGuid().ToString("N"),
                    CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
                });
                db.Sessions.Add(session);
                return Task.CompletedTask;
            },
            "CK_sessions_no_active_refresh_token_when_revoked",
            "A revoked session must not have an active refresh token when the transaction commits");

        await using (var db = ResetDatabase())
        {
            var session = NewSession(revokedAt: DateTimeOffset.UtcNow, reason: (int)SessionRevocationReason.Manual);
            session.RefreshTokens.Add(new RefreshTokenRow
            {
                Id = Guid.NewGuid(), SessionId = session.Id, Hash = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), RevokedAt = DateTimeOffset.UtcNow
            });
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
        }

        await using (var db = ResetDatabase())
        {
            var session = NewSession();
            session.RefreshTokens.Add(new RefreshTokenRow
            {
                Id = Guid.NewGuid(), SessionId = session.Id, Hash = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
            });
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Concurrent_rotation_allows_one_winner_and_one_reuse()
    {
        await using (var setup = Create()) { await setup.Database.EnsureDeletedAsync(); await setup.Database.MigrateAsync(); }
        var user = new User(Guid.NewGuid(), new Email($"{Guid.NewGuid():N}@example.com"), Hash("hash"));
        var now = DateTimeOffset.UtcNow;
        var initial = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid().ToString("N"), now, now.AddHours(1));
        var session = new Session(initial.Id == Guid.Empty ? Guid.NewGuid() : initial.SessionId, user.Id, now, now.AddHours(1), [initial]);
        await using (var seed = Create()) { seed.Users.Add(new UserRow { Id = user.Id, Email = user.Email.Value, PasswordHash = user.PasswordHash.Value, Role = (int)user.Role, IsActive = true, Version = 1 }); seed.Sessions.Add(new SessionRow { Id = session.Id, UserId = user.Id, CreatedAt = now, ExpiresAt = now.AddHours(1), Version = 1, RefreshTokens = [new RefreshTokenRow { Id = initial.Id, SessionId = session.Id, Hash = initial.Hash, CreatedAt = now, ExpiresAt = now.AddHours(1) }] }); await seed.SaveChangesAsync(); }
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
            var uow = new EfUnitOfWork(db);
            await uow.ExecuteInTransactionAsync<int>(async ct => { db.AuditEvents.Add(new AuditEventRow { Action = "rollback_probe", CreatedAt = DateTimeOffset.UtcNow }); await Task.CompletedTask; return new(7, TransactionDecision.Rollback); });
            Assert.Empty(db.ChangeTracker.Entries());
        }
        await using var verify = Create(); Assert.Equal(0, await verify.AuditEvents.CountAsync(x => x.Action == "rollback_probe"));
    }

    [Fact]
    public async Task Unique_email_violation_is_technical_and_concurrent_insert_has_one_winner()
    {
        await using (var setup = Create()) { await setup.Database.EnsureDeletedAsync(); await setup.Database.MigrateAsync(); }
        var id = Guid.NewGuid(); var domain = new User(id, new Email($"{Guid.NewGuid():N}@example.com"), Hash("hash"));
        await using (var seed = Create()) { seed.Users.Add(new UserRow { Id = id, Email = domain.Email.Value, PasswordHash = domain.PasswordHash.Value, Role = 0, IsActive = true, Version = 1 }); await seed.SaveChangesAsync(); }
        await using var firstDb = Create(); var first = new UserRepository(firstDb);
         var duplicate = await Assert.ThrowsAsync<Aegis.Domain.Repositories.UniqueConstraintViolationException>(() =>
             InTransaction(firstDb, async ct => { await first.AddAsync(new User(Guid.NewGuid(), domain.Email, Hash("hash")), ct); return 0; }));
         Assert.DoesNotContain("email", duplicate.Message, StringComparison.OrdinalIgnoreCase);
         var current = await first.GetByIdAsync(id); var changed = User.Rehydrate(current!.Id, current.Email, Hash("new-hash"), current.Role, current.IsActive, current.Version); var winner = await InTransaction(firstDb, ct => first.UpdateAtomicallyAsync(changed, 1, ct)); Assert.True(winner.IsSuccess);
         await using var secondDb = Create(); var second = new UserRepository(secondDb); var stale = User.Rehydrate(id, domain.Email, Hash("stale"), domain.Role, true, 1); var loser = await InTransaction(secondDb, ct => second.UpdateAtomicallyAsync(stale, 1, ct)); Assert.Equal(Aegis.Domain.Repositories.UserUpdateCode.ConcurrencyConflict, loser.Code);
        var concurrentEmail = new Email($"{Guid.NewGuid():N}@example.com");
         var inserts = await Task.WhenAll(Insert(concurrentEmail), Insert(concurrentEmail));
         Assert.Single(inserts, x => x);
         Assert.Single(inserts, x => !x);
    }

    [Fact]
    public async Task Create_and_revoke_all_serialize_on_same_user_lock()
    {
        await using (var setup = Create()) { await setup.Database.EnsureDeletedAsync(); await setup.Database.MigrateAsync(); }
        var userId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; await using (var seed = Create()) { seed.Users.Add(new UserRow { Id = userId, Email = $"{Guid.NewGuid():N}@example.com", PasswordHash = "hash", Role = 0, IsActive = true, Version = 1 }); await seed.SaveChangesAsync(); }
        var sessionId = Guid.NewGuid(); var refresh = new RefreshToken(Guid.NewGuid(), sessionId, Guid.NewGuid().ToString("N"), now, now.AddHours(1)); var session = new Session(sessionId, userId, now, now.AddHours(1), [refresh]);
         var create = Task.Run(async () => { await using var db = Create(); var repo = new SessionRepository(db); return await InTransaction(db, ct => repo.AddIfUserActiveAtomicallyAsync(session, 1, ct)); });
         var revoke = Task.Run(async () => { await using var db = Create(); var repo = new SessionRepository(db); return await InTransaction(db, ct => repo.RevokeAllByUserIdAtomicallyAsync(userId, now, SessionRevocationReason.Manual, ct)); });
        await Task.WhenAll(create, revoke); var createResult = await create; var revokeResult = await revoke;
        await using var verify = Create(); var persisted = await verify.Sessions.Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.Id == sessionId); Assert.True(createResult.IsSuccess); Assert.True(revokeResult.IsSuccess); if (persisted?.RevokedAt is not null) Assert.All(persisted.RefreshTokens, x => Assert.NotNull(x.RevokedAt)); else if (persisted is not null) Assert.All(persisted.RefreshTokens, x => Assert.Null(x.RevokedAt));
    }

    private async Task<Aegis.Domain.Repositories.SessionRotationResult> Rotate(Guid id, string hash, string replacementHash, DateTimeOffset now)
    {
        await using var db = Create(); var repo = new SessionRepository(db);
        var replacement = new RefreshToken(Guid.NewGuid(), id, replacementHash, now, now.AddMinutes(30));
         return await InTransaction(db, ct => repo.RotateAndPersistAtomicallyAsync(id, hash, replacement, now, 1, ct));
    }
     private async Task<Aegis.Domain.Repositories.SessionOperationResult> Revoke(string hash, DateTimeOffset now) { await using var db = Create(); return await InTransaction(db, ct => new SessionRepository(db).RevokeByRefreshTokenHashAtomicallyAsync(hash, now, SessionRevocationReason.Manual, ct)); }
    private AegisDbContext Create() => _fixture.CreateDbContext();
    private AegisDbContext ResetDatabase()
    {
        var db = Create();
        db.Database.EnsureDeleted();
        db.Database.Migrate();
        db.Users.Add(new UserRow { Id = _testUserId, Email = $"{_testUserId:N}@example.com", PasswordHash = "hash", Role = 0, IsActive = true, Version = 1 });
        db.SaveChanges();
        return db;
    }

    private readonly Guid _testUserId = Guid.NewGuid();

    private SessionRow NewSession(DateTimeOffset? revokedAt = null, int? reason = null) => new()
    {
        Id = Guid.NewGuid(), UserId = _testUserId, CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1), RevokedAt = revokedAt, RevocationReason = reason, Version = 1
    };

    private async Task AssertCheckConstraintViolation(Func<AegisDbContext, Task> arrange, string constraint, string message)
    {
        await using var db = ResetDatabase();
        await arrange(db);
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(exception.ToString().Contains(constraint, StringComparison.Ordinal), message);
    }

    private async Task AssertDeferredConstraintViolation(Func<AegisDbContext, Task> arrange, string constraint, string message)
    {
        await using var db = ResetDatabase();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await arrange(db);
        await db.SaveChangesAsync();
        var exception = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
        Assert.True(exception.ToString().Contains(constraint, StringComparison.Ordinal), message);
    }
      private async Task<bool> Insert(Email email)
      {
          await using var db = Create();
          try
          {
               await InTransaction(db, async ct => { await new UserRepository(db).AddAsync(new User(Guid.NewGuid(), email, Hash("hash")), ct); return 0; });
              return true;
          }
          catch (Aegis.Domain.Repositories.UniqueConstraintViolationException)
          {
              return false;
          }
      }
     private static async Task<T> InTransaction<T>(AegisDbContext db, Func<CancellationToken, Task<T>> operation)
     { var uow = new EfUnitOfWork(db); return await uow.ExecuteInTransactionAsync(async ct => new TransactionOutcome<T>(await operation(ct), TransactionDecision.Commit)); }
    private static PasswordHash Hash(string value) => PasswordHash.Create(value).Value!;
}
