namespace Aegis.Domain.Enums;

public enum SessionRevocationReason
{
    /// <summary>Revogação solicitada manualmente.</summary>
    Manual = 0,

    /// <summary>Reutilização de um refresh token já consumido.</summary>
    RefreshTokenReuse = 1,

    /// <summary>Senha do usuário alterada.</summary>
    PasswordChanged = 2,

    /// <summary>Usuário desativado ou ausente durante a operação.</summary>
    UserDeactivated = 3
}
