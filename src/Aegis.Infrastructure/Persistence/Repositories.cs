using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aegis.Infrastructure.Persistence;

public sealed class UserRepository(AegisDbContext db, TransactionRunner transactions) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => ToDomain(await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct));
    public async Task<User?> GetByEmailAsync(Email email, CancellationToken ct = default) => ToDomain(await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email.Value, ct));
    public async Task AddAsync(User user, CancellationToken ct = default) { db.Users.Add(ToRow(user)); await db.SaveChangesAsync(ct); }
    public Task<UserInsertResult> AddIfNotExistsAtomicallyAsync(User user, CancellationToken ct = default) => transactions.ExecuteAsync<UserInsertResult>(async token =>
    {
        try { if (await db.Users.AnyAsync(x => x.Email == user.Email.Value, token)) return new(UserInsertCode.DuplicateEmail); db.Users.Add(ToRow(user)); await db.SaveChangesAsync(token); return new(UserInsertCode.Succeeded); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { db.ChangeTracker.Clear(); return new(UserInsertCode.DuplicateEmail); }
    }, ct);
    public async Task<UserUpdateResult> UpdateAtomicallyAsync(User user, long expectedVersion, CancellationToken ct = default)
    {
        var row = await db.Users.SingleOrDefaultAsync(x => x.Id == user.Id, ct); if (row is null) return new(UserUpdateCode.NotFound);
        if (row.Version != expectedVersion) return new(UserUpdateCode.ConcurrencyConflict);
        row.Email = user.Email.Value; row.PasswordHash = user.PasswordHash; row.Role = (int)user.Role; row.IsActive = user.IsActive; row.Version = expectedVersion + 1;
        db.Entry(row).Property(x => x.Version).OriginalValue = expectedVersion;
        try { await db.SaveChangesAsync(ct); return new(UserUpdateCode.Succeeded); } catch (DbUpdateConcurrencyException) { return new(UserUpdateCode.ConcurrencyConflict); }
    }
    internal static UserRow ToRow(User x) => new() { Id = x.Id, Email = x.Email.Value, PasswordHash = x.PasswordHash, Role = (int)x.Role, IsActive = x.IsActive, Version = x.Version };
    internal static User? ToDomain(UserRow? x) => x is null ? null : User.Rehydrate(x.Id, new Email(x.Email), x.PasswordHash, (UserRole)x.Role, x.IsActive, x.Version);
}

public sealed class SessionRepository(AegisDbContext db, TransactionRunner transactions) : ISessionRepository
{
    public async Task<Session?> GetByRefreshTokenHashWithHistoryAsync(string hash, CancellationToken ct = default) => ToDomain(await db.Sessions.AsNoTracking().Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.RefreshTokens.Any(t => t.Hash == hash), ct));
    public async Task AddAsync(Session session, CancellationToken ct = default) { db.Sessions.Add(ToRow(session)); await db.SaveChangesAsync(ct); }
    public Task<SessionCreationResult> AddIfUserActiveAtomicallyAsync(Session session, long expectedUserVersion, CancellationToken ct = default) => transactions.ExecuteAsync<SessionCreationResult>(async token =>
    { var user = await LockUserAsync(session.UserId, token); if (user is null || !user.IsActive) return new(SessionCreationCode.UserNotFoundOrInactive); if (user.Version != expectedUserVersion) return new(SessionCreationCode.ConcurrencyConflict); db.Sessions.Add(ToRow(session)); await db.SaveChangesAsync(token); return new(SessionCreationCode.Succeeded); }, ct);
    public Task<SessionRotationResult> RotateAndPersistAtomicallyAsync(Guid id, string presentedHash, RefreshToken replacement, DateTimeOffset now, long expectedVersion, CancellationToken ct = default) => transactions.ExecuteAsync<SessionRotationResult>(async token =>
    {
        var userId = await db.Sessions.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(token); if (userId is null || await LockUserAsync(userId.Value, token) is null) return new(SessionRotationCode.NotFound);
        var row = await db.Sessions.Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.Id == id, token); if (row is null) return new(SessionRotationCode.NotFound);
        var domain = ToDomain(row)!;
        if (domain.RefreshTokens.Any(x => x.Hash == presentedHash && x.RevokedAt is not null)) { domain.Revoke(now, SessionRevocationReason.RefreshTokenReuse); Copy(row, domain); await db.SaveChangesAsync(token); return new(SessionRotationCode.RefreshTokenReuse, DomainResult.Failure(DomainErrorCode.RefreshTokenReuse)); }
        if (row.Version != expectedVersion) return new(SessionRotationCode.ConcurrencyConflict);
        var result = domain.Rotate(presentedHash, replacement, now); if (result.IsFailure) return new(Map(result), result); db.ChangeTracker.Clear();
        var changed = await db.Sessions.Where(x => x.Id == id && x.Version == expectedVersion).ExecuteUpdateAsync(x => x.SetProperty(y => y.Version, expectedVersion + 1), token);
        if (changed != 1)
        {
            db.ChangeTracker.Clear();
            var latest = await db.Sessions.Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.Id == id, token);
            if (latest is not null && latest.RefreshTokens.Any(x => x.Hash == presentedHash && x.RevokedAt is not null))
            { var latestDomain = ToDomain(latest)!; latestDomain.Revoke(now, SessionRevocationReason.RefreshTokenReuse); Copy(latest, latestDomain); await db.SaveChangesAsync(token); return new(SessionRotationCode.RefreshTokenReuse, DomainResult.Failure(DomainErrorCode.RefreshTokenReuse)); }
            return new(SessionRotationCode.ConcurrencyConflict);
        }
        var old = domain.RefreshTokens.Single(x => x.Hash == presentedHash);
        await db.RefreshTokens.Where(x => x.Id == old.Id && x.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(y => y.RevokedAt, now), token);
        db.RefreshTokens.Add(new RefreshTokenRow { Id = replacement.Id, SessionId = replacement.SessionId, Hash = replacement.Hash, CreatedAt = replacement.CreatedAt, ExpiresAt = replacement.ExpiresAt });
        try { await db.SaveChangesAsync(token); return new(SessionRotationCode.Succeeded); } catch (DbUpdateConcurrencyException) { return new(SessionRotationCode.ConcurrencyConflict); }
    }, ct);
    public Task<SessionOperationResult> RevokeAndPersistAtomicallyAsync(Guid id, DateTimeOffset now, SessionRevocationReason reason, long expectedVersion, CancellationToken ct = default) => transactions.ExecuteAsync<SessionOperationResult>(async token =>
    { var userId = await db.Sessions.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(token); if (userId is null || await LockUserAsync(userId.Value, token) is null) return new(SessionOperationCode.NotFound); var row = await db.Sessions.Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.Id == id, token); if (row is null) return new(SessionOperationCode.NotFound); if (row.Version != expectedVersion) return new(SessionOperationCode.ConcurrencyConflict); var d = ToDomain(row)!; var r = d.Revoke(now, reason); if (r.IsFailure) return new(SessionOperationCode.DomainFailure, r); Copy(row, d); try { await db.SaveChangesAsync(token); return new(SessionOperationCode.Succeeded); } catch (DbUpdateConcurrencyException) { return new(SessionOperationCode.ConcurrencyConflict); } }, ct);
    public Task<SessionOperationResult> RevokeAllByUserIdAtomicallyAsync(Guid userId, DateTimeOffset now, SessionRevocationReason reason, CancellationToken ct = default) => transactions.ExecuteAsync<SessionOperationResult>(async token =>
    { var user = await LockUserAsync(userId, token); if (user is null) return new(SessionOperationCode.NotFound); var rows = await db.Sessions.Include(x => x.RefreshTokens).Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(token); foreach (var row in rows) { var d = ToDomain(row)!; d.Revoke(now, reason); Copy(row, d); } await db.SaveChangesAsync(token); return new(SessionOperationCode.Succeeded); }, ct);
    public Task<SessionOperationResult> RevokeByRefreshTokenHashAtomicallyAsync(string hash, DateTimeOffset now, SessionRevocationReason reason, CancellationToken ct = default) => transactions.ExecuteAsync<SessionOperationResult>(async token =>
    { var userId = await db.Sessions.AsNoTracking().Where(x => x.RefreshTokens.Any(t => t.Hash == hash)).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(token); if (userId is null || await LockUserAsync(userId.Value, token) is null) return new(SessionOperationCode.Succeeded); var row = await db.Sessions.Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.RefreshTokens.Any(t => t.Hash == hash), token); if (row is null) return new(SessionOperationCode.Succeeded); var d = ToDomain(row)!; var r = d.Revoke(now, reason); if (r.IsFailure) return new(SessionOperationCode.DomainFailure, r); Copy(row, d); await db.SaveChangesAsync(token); return new(SessionOperationCode.Succeeded); }, ct);
    private static SessionRow ToRow(Session x) => new() { Id = x.Id, UserId = x.UserId, CreatedAt = x.CreatedAt, ExpiresAt = x.ExpiresAt, RevokedAt = x.RevokedAt, RevocationReason = x.RevocationReason is null ? null : (int)x.RevocationReason, Version = x.Version, RefreshTokens = x.RefreshTokens.Select(t => new RefreshTokenRow { Id = t.Id, SessionId = t.SessionId, Hash = t.Hash, CreatedAt = t.CreatedAt, ExpiresAt = t.ExpiresAt, RevokedAt = t.RevokedAt }).ToList() };
    private static Session? ToDomain(SessionRow? x) => x is null ? null : Session.Rehydrate(x.Id, x.UserId, x.CreatedAt, x.ExpiresAt, x.RefreshTokens.Select(t => RefreshToken.Rehydrate(t.Id, t.SessionId, t.Hash, t.CreatedAt, t.ExpiresAt, t.RevokedAt)), x.RevokedAt, x.RevocationReason is null ? null : (SessionRevocationReason)x.RevocationReason, x.Version);
    private static void Copy(SessionRow row, Session d) { row.RevokedAt = d.RevokedAt; row.RevocationReason = d.RevocationReason is null ? null : (int)d.RevocationReason; row.Version = d.Version; foreach (var t in d.RefreshTokens) { var old = row.RefreshTokens.SingleOrDefault(x => x.Id == t.Id); if (old is null) row.RefreshTokens.Add(new() { Id = t.Id, SessionId = t.SessionId, Hash = t.Hash, CreatedAt = t.CreatedAt, ExpiresAt = t.ExpiresAt, RevokedAt = t.RevokedAt }); else old.RevokedAt = t.RevokedAt; } }
    private static SessionRotationCode Map(DomainResult x) => x.ErrorCode == DomainErrorCode.RefreshTokenReuse ? SessionRotationCode.RefreshTokenReuse : SessionRotationCode.DomainFailure;
    private async Task<UserRow?> LockUserAsync(Guid userId, CancellationToken ct) => await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE \"Id\" = {userId} FOR UPDATE").SingleOrDefaultAsync(ct);
}
