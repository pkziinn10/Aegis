using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aegis.Infrastructure.Persistence;

public sealed class SessionRepository(AegisDbContext db) : ISessionRepository
{
    public async Task<Session?> GetByRefreshTokenHashWithHistoryAsync(string hash, CancellationToken ct = default) => RepositoryMappings.ToDomain(await db.Sessions.AsNoTracking().Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.RefreshTokens.Any(t => t.Hash == hash), ct));
    public async Task AddAsync(Session session, CancellationToken ct = default) { RequireTransaction(); db.Sessions.Add(RepositoryMappings.ToRow(session)); await db.SaveChangesAsync(ct); }
    public async Task<SessionCreationResult> AddIfUserActiveAtomicallyAsync(Session session, long expectedUserVersion, CancellationToken ct = default)
    { RequireTransaction(); var user = await LockUserAsync(session.UserId, ct); if (user is null || !user.IsActive) return new(SessionCreationCode.UserNotFoundOrInactive); if (user.Version != expectedUserVersion) return new(SessionCreationCode.ConcurrencyConflict); db.Sessions.Add(RepositoryMappings.ToRow(session)); await db.SaveChangesAsync(ct); return new(SessionCreationCode.Succeeded); }
    public async Task<SessionRotationResult> RotateAndPersistAtomicallyAsync(Guid id, string presentedHash, RefreshToken replacement, DateTimeOffset now, long expectedVersion, CancellationToken ct = default)
    {
        RequireTransaction(); var userId = await db.Sessions.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct); if (userId is null || await LockUserAsync(userId.Value, ct) is null) return new(SessionRotationCode.NotFound); var row = await db.Sessions.Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.Id == id, ct); if (row is null) return new(SessionRotationCode.NotFound);
        var domain = RepositoryMappings.ToDomain(row)!; var result = domain.Rotate(presentedHash, replacement, now);
        if (result.IsFailure) { RepositoryMappings.Copy(row, domain); await db.SaveChangesAsync(ct); return new(Map(result), result); }
        if (row.Version != expectedVersion) return new(SessionRotationCode.ConcurrencyConflict);
        var old = domain.RefreshTokens.Single(x => x.Hash == presentedHash); row.RefreshTokens.Single(x => x.Id == old.Id).RevokedAt = old.RevokedAt; row.Version = domain.Version; await db.SaveChangesAsync(ct);
        db.RefreshTokens.Add(new RefreshTokenRow { Id = replacement.Id, SessionId = replacement.SessionId, Hash = replacement.Hash, CreatedAt = replacement.CreatedAt, ExpiresAt = replacement.ExpiresAt }); await db.SaveChangesAsync(ct); return new(SessionRotationCode.Succeeded);
    }
    public async Task<SessionOperationResult> RevokeAndPersistAtomicallyAsync(Guid id, DateTimeOffset now, SessionRevocationReason reason, long expectedVersion, CancellationToken ct = default)
    { RequireTransaction(); var userId = await db.Sessions.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct); if (userId is null || await LockUserAsync(userId.Value, ct) is null) return new(SessionOperationCode.NotFound); var row = await db.Sessions.Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.Id == id, ct); if (row is null) return new(SessionOperationCode.NotFound); if (row.Version != expectedVersion) return new(SessionOperationCode.ConcurrencyConflict); var d = RepositoryMappings.ToDomain(row)!; var r = d.Revoke(now, reason); if (r.IsFailure) return new(SessionOperationCode.DomainFailure, r); RepositoryMappings.Copy(row, d); await db.SaveChangesAsync(ct); return new(SessionOperationCode.Succeeded); }
    public async Task<SessionOperationResult> RevokeAllByUserIdAtomicallyAsync(Guid userId, DateTimeOffset now, SessionRevocationReason reason, CancellationToken ct = default)
    { RequireTransaction(); var user = await LockUserAsync(userId, ct); if (user is null) return new(SessionOperationCode.NotFound); var rows = await db.Sessions.Include(x => x.RefreshTokens).Where(x => x.UserId == userId && x.RevokedAt == null).ToListAsync(ct); foreach (var row in rows) { var d = RepositoryMappings.ToDomain(row)!; d.Revoke(now, reason); RepositoryMappings.Copy(row, d); } await db.SaveChangesAsync(ct); return new(SessionOperationCode.Succeeded); }
    public async Task<SessionOperationResult> RevokeByRefreshTokenHashAtomicallyAsync(string hash, DateTimeOffset now, SessionRevocationReason reason, CancellationToken ct = default)
    { RequireTransaction(); var userId = await db.Sessions.AsNoTracking().Where(x => x.RefreshTokens.Any(t => t.Hash == hash)).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct); if (userId is null || await LockUserAsync(userId.Value, ct) is null) return new(SessionOperationCode.Succeeded); var row = await db.Sessions.Include(x => x.RefreshTokens).SingleOrDefaultAsync(x => x.RefreshTokens.Any(t => t.Hash == hash), ct); if (row is null) return new(SessionOperationCode.Succeeded); var d = RepositoryMappings.ToDomain(row)!; var r = d.Revoke(now, reason); if (r.IsFailure) return new(SessionOperationCode.DomainFailure, r); RepositoryMappings.Copy(row, d); await db.SaveChangesAsync(ct); return new(SessionOperationCode.Succeeded); }
    private static SessionRotationCode Map(DomainResult x) => x.ErrorCode == DomainErrorCode.RefreshTokenReuse ? SessionRotationCode.RefreshTokenReuse : SessionRotationCode.DomainFailure;
    private async Task<UserRow?> LockUserAsync(Guid userId, CancellationToken ct) => await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE \"Id\" = {userId} FOR UPDATE").SingleOrDefaultAsync(ct);
    private void RequireTransaction() { if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Mutating repository operations require an active unit of work transaction."); }
}
