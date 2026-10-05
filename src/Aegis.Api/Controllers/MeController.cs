using Aegis.Api.Security;
using Aegis.Application.Results;
using Aegis.Application.UseCases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aegis.Api.Controllers;

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
