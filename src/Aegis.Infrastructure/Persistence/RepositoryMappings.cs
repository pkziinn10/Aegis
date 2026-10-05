using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;

namespace Aegis.Infrastructure.Persistence;

internal static class RepositoryMappings
{
    public static UserRow ToRow(User x) => new() { Id = x.Id, Email = x.Email.Value, PasswordHash = x.PasswordHash.Value, Role = (int)x.Role, IsActive = x.IsActive, Version = x.Version };
    public static User? ToDomain(UserRow? x) { if (x is null) return null; var passwordHash = PasswordHash.Create(x.PasswordHash); if (passwordHash.IsFailure) throw new InvalidOperationException($"User row '{x.Id}' contains an invalid password hash."); return User.Rehydrate(x.Id, new Email(x.Email), passwordHash.Value!, (UserRole)x.Role, x.IsActive, x.Version); }
    public static SessionRow ToRow(Session x) => new() { Id = x.Id, UserId = x.UserId, CreatedAt = x.CreatedAt, ExpiresAt = x.ExpiresAt, RevokedAt = x.RevokedAt, RevocationReason = x.RevocationReason is null ? null : (int)x.RevocationReason, Version = x.Version, RefreshTokens = x.RefreshTokens.Select(t => new RefreshTokenRow { Id = t.Id, SessionId = t.SessionId, Hash = t.Hash, CreatedAt = t.CreatedAt, ExpiresAt = t.ExpiresAt, RevokedAt = t.RevokedAt }).ToList() };
    public static Session? ToDomain(SessionRow? x) => x is null ? null : Session.Rehydrate(x.Id, x.UserId, x.CreatedAt, x.ExpiresAt, x.RefreshTokens.Select(t => RefreshToken.Rehydrate(t.Id, t.SessionId, t.Hash, t.CreatedAt, t.ExpiresAt, t.RevokedAt)), x.RevokedAt, x.RevocationReason is null ? null : (SessionRevocationReason)x.RevocationReason, x.Version);
    public static void Copy(SessionRow row, Session d) { row.RevokedAt = d.RevokedAt; row.RevocationReason = d.RevocationReason is null ? null : (int)d.RevocationReason; row.Version = d.Version; foreach (var t in d.RefreshTokens) { var old = row.RefreshTokens.SingleOrDefault(x => x.Id == t.Id); if (old is null) row.RefreshTokens.Add(new() { Id = t.Id, SessionId = t.SessionId, Hash = t.Hash, CreatedAt = t.CreatedAt, ExpiresAt = t.ExpiresAt, RevokedAt = t.RevokedAt }); else old.RevokedAt = t.RevokedAt; } }
}
