using Aegis.Application.Results;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Aegis.Api;

public static class ApiErrors
{
    public const string ProblemContentType = "application/problem+json";

    public static ObjectResult From(ControllerBase controller, ApplicationErrorCode code)
        => From(controller.HttpContext, code);

    public static ObjectResult From(HttpContext context, ApplicationErrorCode code)
    {
        var status = code switch
        {
            ApplicationErrorCode.InvalidCredentials or ApplicationErrorCode.Unauthorized or ApplicationErrorCode.InvalidRefreshToken or ApplicationErrorCode.SessionExpired or ApplicationErrorCode.SessionRevoked or ApplicationErrorCode.RefreshTokenReuse => 401,
            ApplicationErrorCode.InactiveUser => 403,
            ApplicationErrorCode.EmailAlreadyRegistered or ApplicationErrorCode.ConcurrencyConflict => 409,
            ApplicationErrorCode.InvalidPasswordHash => 500,
            _ => 400
        };
        return Problem(context, status, status >= 500 ? "InternalServerError" : code.ToString());
    }

    public static ObjectResult Forbidden(ControllerBase controller, ApplicationErrorCode code) => Problem(controller.HttpContext, 403, code.ToString());

    public static ProblemDetails ForStatus(HttpContext context, int status, string code)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.com/{status}",
            Status = status,
            Title = status switch
            {
                401 => "Unauthorized",
                403 => "Forbidden",
                429 => "Too Many Requests",
                500 => "Internal Server Error",
                _ => "Bad Request"
            }
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return problem;
    }

    public static async Task WriteAsync(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = ProblemContentType;
        await context.Response.WriteAsync(JsonSerializer.Serialize(ForStatus(context, status, code)));
    }

    private static ObjectResult Problem(HttpContext context, int status, string publicCode)
    {
        var problem = ForStatus(context, status, publicCode);
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { ProblemContentType } };
    }
}
