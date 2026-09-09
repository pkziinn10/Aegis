using Aegis.Api.Security;
using Aegis.Application.Contracts;
using Aegis.Application.Results;
using Aegis.Application.UseCases;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aegis.Api.Controllers;

public sealed record CredentialsRequest(string Email, string Password)
{
    public override string ToString() => nameof(CredentialsRequest);
}
public sealed record RefreshRequest(string RefreshToken)
{
    public override string ToString() => nameof(RefreshRequest);
}
public sealed record BrowserSessionResponse(Guid UserId, string Email, string Role, string AccessToken, string TokenType, DateTimeOffset AccessTokenExpiresAt, string CsrfToken)
{
    public override string ToString() => nameof(BrowserSessionResponse);
}
public sealed record TokenSessionResponse(Guid UserId, string Email, string Role, string AccessToken, string TokenType, DateTimeOffset AccessTokenExpiresAt, string RefreshToken)
{
    public override string ToString() => nameof(TokenSessionResponse);
}
public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset AccessTokenExpiresAt, string RefreshToken)
{
    public override string ToString() => nameof(TokenResponse);
}
public sealed record UserResponse(Guid Id, string Email, string Role);

internal static class AuthPayloadLimits
{
    public const int CredentialsBytes = 16 * 1024;
    public const int RefreshBytes = 8 * 1024;
}

[ApiController]
[Route("auth")]
public sealed class AuthController(
    RegisterUseCase register,
    LoginUseCase login,
    RefreshUseCase refresh,
    LogoutUseCase logout,
    IAntiforgery antiforgery,
    Aegis.Api.Security.OriginValidator originValidator) : ControllerBase
{
    private const string RefreshCookie = "__Host-refresh-token";

    [HttpPost("browser/register")]
    [EnableRateLimiting(SecurityPolicyNames.OtherOperationByIp)]
    [RequestSizeLimit(AuthPayloadLimits.CredentialsBytes)]
    public async Task<IActionResult> BrowserRegister(CredentialsRequest? request, CancellationToken ct)
    {
        NoStore();
        if (!await ValidateOriginAsync()) return ApiErrors.Forbidden(this, ApplicationErrorCode.InvalidRequest);
        var result = await register.ExecuteAsync(new RegisterCommand(request?.Email ?? "", request?.Password ?? ""), ct);
        if (result.IsFailure) return ApiErrors.From(this, result.ErrorCode);
        return BrowserSession(result.Value!.User, result.Value.Tokens);
    }

    [HttpPost("browser/login")]
    [EnableRateLimiting(SecurityPolicyNames.LoginByIp)]
    [ServiceFilter(typeof(AccountRateLimitFilter))]
    [RequestSizeLimit(AuthPayloadLimits.CredentialsBytes)]
    public async Task<IActionResult> BrowserLogin(CredentialsRequest? request, CancellationToken ct)
    {
        NoStore();
        if (!await ValidateOriginAsync()) return ApiErrors.Forbidden(this, ApplicationErrorCode.InvalidRequest);
        var result = await login.ExecuteAsync(new LoginCommand(request?.Email ?? "", request?.Password ?? ""), ct);
        if (result.IsFailure) return ApiErrors.From(this, result.ErrorCode);
        return BrowserSession(result.Value!.User, result.Value.Tokens);
    }

    [HttpPost("browser/refresh")]
    [EnableRateLimiting(SecurityPolicyNames.OtherOperationByIp)]
    [RequestSizeLimit(AuthPayloadLimits.RefreshBytes)]
    public async Task<IActionResult> BrowserRefresh(CancellationToken ct)
    {
        NoStore();
        if (!await ValidateOriginAsync() || !await ValidateCsrfAsync()) return ApiErrors.Forbidden(this, ApplicationErrorCode.InvalidRequest);
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var value) || string.IsNullOrWhiteSpace(value))
        {
            DeleteAuthCookies();
            return ApiErrors.From(this, ApplicationErrorCode.InvalidCredentials);
        }
        var result = await refresh.ExecuteAsync(new RefreshCommand(value), ct);
        if (result.IsFailure)
        {
            if (BrowserCookiePolicy.IsTerminalRefreshFailure(result.ErrorCode)) DeleteAuthCookies();
            return ApiErrors.From(this, result.ErrorCode);
        }
        SetRefreshCookie(result.Value!.RefreshToken.Value);
        return BrowserToken(result.Value, await IssueCsrfTokenAsync());
    }

    [HttpPost("browser/logout")]
    [EnableRateLimiting(SecurityPolicyNames.OtherOperationByIp)]
    public async Task<IActionResult> BrowserLogout(CancellationToken ct)
    {
        NoStore();
        if (!await ValidateOriginAsync() || !await ValidateCsrfAsync()) return ApiErrors.Forbidden(this, ApplicationErrorCode.InvalidRequest);
        try
        {
            if (Request.Cookies.TryGetValue(RefreshCookie, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                var result = await logout.ExecuteAsync(new LogoutCommand(value), ct);
                if (result.IsFailure) return ApiErrors.From(this, result.ErrorCode);
            }
            return NoContent();
        }
        catch
        {
            DeleteAuthCookies();
            return ApiErrors.From(HttpContext, ApplicationErrorCode.InvalidPasswordHash);
        }
        finally
        {
            DeleteAuthCookies();
        }
    }

    [HttpPost("token/register")]
    [EnableRateLimiting(SecurityPolicyNames.OtherOperationByIp)]
    [RequestSizeLimit(AuthPayloadLimits.CredentialsBytes)]
    public async Task<IActionResult> TokenRegister(CredentialsRequest? request, CancellationToken ct)
    {
        var result = await register.ExecuteAsync(new RegisterCommand(request?.Email ?? "", request?.Password ?? ""), ct);
        return result.IsFailure ? ApiErrors.From(this, result.ErrorCode) : TokenSession(result.Value!.User, result.Value.Tokens);
    }

    [HttpPost("token/login")]
    [EnableRateLimiting(SecurityPolicyNames.LoginByIp)]
    [ServiceFilter(typeof(AccountRateLimitFilter))]
    [RequestSizeLimit(AuthPayloadLimits.CredentialsBytes)]
    public async Task<IActionResult> TokenLogin(CredentialsRequest? request, CancellationToken ct)
    {
        var result = await login.ExecuteAsync(new LoginCommand(request?.Email ?? "", request?.Password ?? ""), ct);
        return result.IsFailure ? ApiErrors.From(this, result.ErrorCode) : TokenSession(result.Value!.User, result.Value.Tokens);
    }

    [HttpPost("token/refresh")]
    [EnableRateLimiting(SecurityPolicyNames.OtherOperationByIp)]
    [RequestSizeLimit(AuthPayloadLimits.RefreshBytes)]
    public async Task<IActionResult> TokenRefresh(RefreshRequest? request, CancellationToken ct)
    {
        var result = await refresh.ExecuteAsync(new RefreshCommand(request?.RefreshToken), ct);
        return result.IsFailure ? ApiErrors.From(this, result.ErrorCode) : TokenResult(result.Value!);
    }

    [HttpPost("token/logout")]
    [EnableRateLimiting(SecurityPolicyNames.OtherOperationByIp)]
    [RequestSizeLimit(AuthPayloadLimits.RefreshBytes)]
    public async Task<IActionResult> TokenLogout(RefreshRequest? request, CancellationToken ct)
    {
        var result = await logout.ExecuteAsync(new LogoutCommand(request?.RefreshToken), ct);
        return result.IsFailure ? ApiErrors.From(this, result.ErrorCode) : NoContent();
    }

    private async Task<bool> ValidateOriginAsync() => originValidator.IsAllowed(Request.Headers.Origin.ToString());
    private Task<bool> ValidateCsrfAsync() => antiforgery.IsRequestValidAsync(HttpContext);
    private async Task<string> IssueCsrfTokenAsync() => antiforgery.GetAndStoreTokens(HttpContext).RequestToken!;
    private IActionResult BrowserSession(UserDto user, TokenResult tokens)
    { SetRefreshCookie(tokens.RefreshToken.Value); return BrowserToken(tokens, IssueCsrfTokenAsync().GetAwaiter().GetResult(), user); }
    private IActionResult BrowserToken(TokenResult tokens, string csrf, UserDto? user = null) => NoStore(Ok(user is null ? new { accessToken = tokens.AccessToken.Value, tokenType = tokens.AccessToken.Type, accessTokenExpiresAt = tokens.AccessToken.ExpiresAt, csrfToken = csrf } : new BrowserSessionResponse(user.Id, user.Email, user.Role.ToString(), tokens.AccessToken.Value, tokens.AccessToken.Type, tokens.AccessToken.ExpiresAt, csrf)));
    private IActionResult TokenSession(UserDto user, TokenResult tokens) => NoStore(Ok(new TokenSessionResponse(user.Id, user.Email, user.Role.ToString(), tokens.AccessToken.Value, tokens.AccessToken.Type, tokens.AccessToken.ExpiresAt, tokens.RefreshToken.Value)));
    private IActionResult TokenResult(TokenResult tokens) => NoStore(Ok(new TokenResponse(tokens.AccessToken.Value, tokens.AccessToken.Type, tokens.AccessToken.ExpiresAt, tokens.RefreshToken.Value)));
    private void NoStore() => Response.Headers.CacheControl = "no-store";
    private IActionResult NoStore(IActionResult result) { NoStore(); return result; }
    private void SetRefreshCookie(string value) => Response.Cookies.Append(RefreshCookie, value, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
    private void DeleteAuthCookies() => BrowserCookiePolicy.Delete(Response);
}

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

[ApiController]
[Route("api/auth")]
public sealed class MeController(GetMeUseCase getMe) : ControllerBase
{
    [HttpGet("me")]
    [Authorize(Policy = SecurityPolicyNames.AuthenticatedUser)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await getMe.ExecuteAsync(ct);
        return result.IsFailure ? ApiErrors.From(this, result.ErrorCode) : Ok(new UserResponse(result.Value!.Id, result.Value.Email, result.Value.Role.ToString()));
    }
}
