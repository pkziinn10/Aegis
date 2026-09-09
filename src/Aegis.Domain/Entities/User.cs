using Aegis.Domain.Enums;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;

namespace Aegis.Domain.Entities;

public sealed class User
{
    public User(Guid id, Email email, string passwordHash, UserRole role = UserRole.User, long version = 1)
        : this(id, email, passwordHash, role, true, version) { }

    public static User Rehydrate(Guid id, Email email, string passwordHash, UserRole role, bool isActive, long version) =>
        new(id, email, passwordHash, role, isActive, version);

    private User(Guid id, Email email, string passwordHash, UserRole role, bool isActive, long version)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identidade inválida.", nameof(id));
        if (passwordHash is null) throw new ArgumentNullException(nameof(passwordHash));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("Hash de senha inválido.", nameof(passwordHash));
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
    public string PasswordHash { get; private set; }
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public long Version { get; private set; }

    public DomainResult ChangeEmail(Email email)
    {
        if (email is null) return DomainResult.Failure(DomainErrorCode.InvalidEmail);
        if (Email == email) return DomainResult.Success();
        Email = email;
        Version++;
        return DomainResult.Success();
    }

    public DomainResult ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            return DomainResult.Failure(DomainErrorCode.InvalidPasswordHash);
        if (PasswordHash == passwordHash) return DomainResult.Success();
        PasswordHash = passwordHash;
        Version++;
        return DomainResult.Success();
    }

    public void ChangeRole(UserRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (Role == role) return;
        Role = role;
        Version++;
    }

    public void Deactivate()
    {
        if (!IsActive) return;
        IsActive = false;
        Version++;
    }

    public void Activate()
    {
        if (IsActive) return;
        IsActive = true;
        Version++;
    }
}
