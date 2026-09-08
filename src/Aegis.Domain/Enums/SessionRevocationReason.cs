namespace Aegis.Domain.Enums;

public enum SessionRevocationReason
{
    Manual = 0,
    RefreshTokenReuse = 1,
    PasswordChanged = 2,
    UserDeactivated = 3
}
