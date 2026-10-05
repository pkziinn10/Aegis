using Aegis.Domain.Enums;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;

namespace Aegis.Domain.Entities;

public sealed class User
{
    public User(Guid id, Email email, PasswordHash passwordHash, UserRole role = UserRole.User, long version = 1)
        : this(id, email, passwordHash, role, true, version) { }

    public static User Rehydrate(Guid id, Email email, PasswordHash passwordHash, UserRole role, bool isActive, long version) =>
        new(id, email, passwordHash, role, isActive, version);

    private User(Guid id, Email email, PasswordHash passwordHash, UserRole role, bool isActive, long version)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identidade inválida.", nameof(id));
        if (passwordHash is null) throw new ArgumentNullException(nameof(passwordHash));
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        Id = id;
        Email = email ?? throw new ArgumentNullException(nameof(email));
        PasswordHash = passwordHash;
        Role = role;
        IsActive = isActive;
        Version = version;
    }

    public Guid Id { get; }
    public Email Email { get; private set; }
    public PasswordHash PasswordHash { get; private set; }
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public long Version { get; private set; }

    public DomainResult CanAuthenticate()
    {
        var accountIsActive = IsActive;
        if (!accountIsActive) return DomainResult.Failure(DomainErrorCode.AccountInactive);
        return DomainResult.Success();
    }

    public DomainResult CanChangePassword()
    {
        var accountIsActive = IsActive;
        if (!accountIsActive) return DomainResult.Failure(DomainErrorCode.AccountInactive);
        return DomainResult.Success();
    }

    public DomainResult ChangeEmail(Email email)
    {
        if (email is null) return DomainResult.Failure(DomainErrorCode.InvalidEmail);
        if (Email == email) return DomainResult.Success();
        Email = email;
        Version++;
        return DomainResult.Success();
    }

    public DomainResult ChangePasswordHash(PasswordHash passwordHash)
    {
        if (passwordHash is null) return DomainResult.Failure(DomainErrorCode.InvalidPasswordHash);
        var canChangePassword = CanChangePassword();
        if (canChangePassword.IsFailure) return canChangePassword;
        if (PasswordHash == passwordHash) return DomainResult.Success();
        PasswordHash = passwordHash;
        Version++;
        return DomainResult.Success();
    }

    public DomainResult ChangeRole(UserRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        var roleIsUnchanged = Role == role;
        if (roleIsUnchanged) return DomainResult.Success();
        Role = role;
        Version++;
        return DomainResult.Success();
    }

    public DomainResult Deactivate()
    {
        var accountIsAlreadyInactive = !IsActive;
        if (accountIsAlreadyInactive) return DomainResult.Failure(DomainErrorCode.UserAlreadyDeactivated);
        IsActive = false;
        Version++;
        return DomainResult.Success();
    }

    public DomainResult Activate()
    {
        var accountIsAlreadyActive = IsActive;
        if (accountIsAlreadyActive) return DomainResult.Success();
        IsActive = true;
        Version++;
        return DomainResult.Success();
    }
}
