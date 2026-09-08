using Aegis.Domain.Entities;
using Aegis.Domain.ValueObjects;

namespace Aegis.Domain.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task<UserInsertResult> AddIfNotExistsAtomicallyAsync(User user,
        CancellationToken cancellationToken = default);
    Task<UserUpdateResult> UpdateAtomicallyAsync(User user, long expectedVersion,
        CancellationToken cancellationToken = default);
}

public enum UserInsertCode
{
    Succeeded = 0,
    DuplicateEmail
}

public sealed record UserInsertResult(UserInsertCode Code)
{
    public bool IsSuccess => Code == UserInsertCode.Succeeded;
}

public enum UserUpdateCode
{
    Succeeded = 0,
    NotFound,
    ConcurrencyConflict
}

public sealed record UserUpdateResult(UserUpdateCode Code)
{
    public bool IsSuccess => Code == UserUpdateCode.Succeeded;
}
