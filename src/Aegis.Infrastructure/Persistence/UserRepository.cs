using Aegis.Domain.Entities;
using Aegis.Domain.Enums;
using Aegis.Domain.Repositories;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Aegis.Infrastructure.Persistence;

public sealed class UserRepository(AegisDbContext db) : IUserRepository
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) => RepositoryMappings.ToDomain(await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct));
    public async Task<User?> GetByEmailAsync(Email email, CancellationToken ct = default) => RepositoryMappings.ToDomain(await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email.Value, ct));
    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        RequireTransaction();
        db.Users.Add(RepositoryMappings.ToRow(user));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new UniqueConstraintViolationException(ex);
        }
    }
    public async Task<UserUpdateResult> UpdateAtomicallyAsync(User user, long expectedVersion, CancellationToken ct = default)
    { RequireTransaction(); var row = await db.Users.SingleOrDefaultAsync(x => x.Id == user.Id, ct); if (row is null) return new(UserUpdateCode.NotFound); if (row.Version != expectedVersion) return new(UserUpdateCode.ConcurrencyConflict); row.Email = user.Email.Value; row.PasswordHash = user.PasswordHash.Value; row.Role = (int)user.Role; row.IsActive = user.IsActive; row.Version = expectedVersion + 1; db.Entry(row).Property(x => x.Version).OriginalValue = expectedVersion; try { await db.SaveChangesAsync(ct); return new(UserUpdateCode.Succeeded); } catch (DbUpdateConcurrencyException) { return new(UserUpdateCode.ConcurrencyConflict); } }
    private void RequireTransaction() { if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Mutating repository operations require an active unit of work transaction."); }
}
