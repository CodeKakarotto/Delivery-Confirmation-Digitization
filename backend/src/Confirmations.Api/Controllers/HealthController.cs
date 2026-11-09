using Microsoft.AspNetCore.Mvc;

namespace Confirmations.Api.Controllers;

/// <summary>
/// Simple "is the API running?" check. Returns no data about
/// confirmations, scans, or users, so it needs no login.
/// </summary>
[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "OK" });
}
