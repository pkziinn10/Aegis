using Aegis.Domain.Enums;
using Aegis.Domain.Results;
using Aegis.Domain.ValueObjects;

namespace Aegis.Domain.Entities;

public sealed class User
{
    public User(Guid id, Email email, UserRole role = UserRole.User)
    {
        if (id == Guid.Empty) throw new ArgumentException("Identidade inválida.", nameof(id));
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        Id = id;
        Email = email ?? throw new ArgumentNullException(nameof(email));
        Role = role;
        IsActive = true;
    }

    public Guid Id { get; }
    public Email Email { get; private set; }
    public UserRole Role { get; private set; }
    public bool IsActive { get; private set; }

    public DomainResult ChangeEmail(Email email)
    {
        if (email is null) return DomainResult.Failure(DomainErrorCode.InvalidEmail);
        Email = email;
        return DomainResult.Success();
    }

    public void ChangeRole(UserRole role)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        Role = role;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
