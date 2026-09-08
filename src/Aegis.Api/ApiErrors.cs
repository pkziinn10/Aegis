using Aegis.Application.Results;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Api;

public static class ApiErrors
{
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

    private static ObjectResult Problem(HttpContext context, int status, string publicCode)
    {
        var problem = new ProblemDetails { Status = status, Title = status switch { 401 => "Unauthorized", 403 => "Forbidden", 409 => "Conflict", 500 => "Internal Server Error", _ => "Bad Request" } };
        problem.Extensions["code"] = publicCode;
        problem.Extensions["traceId"] = context.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    }
}
