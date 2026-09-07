using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aegis.Api.IntegrationTests;

[ApiController]
[Route("integration")]
public sealed class TestEndpointsController : ControllerBase
{
    [HttpGet("public")]
    public IActionResult Public() => Ok(new { ok = true });

    [Authorize]
    [HttpGet("protected")]
    public IActionResult Protected() => Ok(new { subject = User.Identity?.Name });

    [Authorize(Roles = "admin")]
    [HttpGet("role")]
    public IActionResult Role() => Ok();

    [HttpGet("request-info")]
    public IActionResult RequestInfo() => Ok(new
    {
        scheme = Request.Scheme,
        ip = HttpContext.Connection.RemoteIpAddress?.ToString()
    });

    [EnableRateLimiting("AuthByIp")]
    [AllowAnonymous]
    [HttpPost("limited")]
    public IActionResult Limited() => Ok();

    [HttpGet("cookie")]
    public IActionResult Cookie()
    {
        Response.Cookies.Append("integration", "value");
        return NoContent();
    }
}
