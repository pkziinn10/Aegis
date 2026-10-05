using Aegis.Application.Results;
using Microsoft.AspNetCore.Http;

namespace Aegis.Api.Controllers;

public static class BrowserCookiePolicy
{
    public static bool IsTerminalRefreshFailure(ApplicationErrorCode code) => code is
        ApplicationErrorCode.InvalidCredentials or ApplicationErrorCode.InvalidRefreshToken or
        ApplicationErrorCode.RefreshTokenReuse or ApplicationErrorCode.SessionExpired or
        ApplicationErrorCode.SessionRevoked;

    public static void Delete(HttpResponse response)
    {
        var options = new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" };
        response.Cookies.Delete("__Host-refresh-token", options);
        response.Cookies.Delete("__Host-csrf-token", options);
    }
}
