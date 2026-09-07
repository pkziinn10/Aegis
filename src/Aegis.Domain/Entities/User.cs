using Aegis.Domain.Enums;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;

namespace Aegis.Domain.Entities;

public sealed class User
{
    public User(Guid id, Email email, UserRole role = UserRole.User, long version = 1)
        : this(id, email, role, true, version) { }

    public static User Rehydrate(Guid id, Email email, UserRole role, bool isActive, long version) =>
        new(id, email, role, isActive, version);

    private User(Guid id, Email email, UserRole role, bool isActive, long version)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identidade inválida.", nameof(id));
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        Id = id;
        Email = email ?? throw new ArgumentNullException(nameof(email));
        Role = role;
        IsActive = isActive;
        Version = version;
    }

    public Guid Id { get; }
    public Email Email { get; private set; }
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
