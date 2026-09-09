namespace Aegis.Api.Security;

public static class SecurityPolicyNames
{
    public const string AuthByIp = "AuthByIp";
    public const string OtherOperationByIp = "OtherOperationByIp";
    public const string LoginByIp = "LoginByIp";
    public const string AccountLogin = "AccountLogin";
    public const string BrowserLoginByIp = "BrowserLoginByIp";
    public const string TokenLoginByIp = "TokenLoginByIp";
    public const string BrowserOperationByIp = "BrowserOperationByIp";
    public const string TokenOperationByIp = "TokenOperationByIp";
    public const string AuthenticatedUser = "AuthenticatedUser";
    public const string AdminOnly = "AdminOnly";
}
