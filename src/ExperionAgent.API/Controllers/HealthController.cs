using Microsoft.AspNetCore.Mvc;

namespace ExperionAgent.API.Controllers;

[ApiController]
[Route("api")]
public class HealthController : ControllerBase
{
    [HttpGet("health")]
    public ActionResult Health() => Ok(new
    {
        status = "healthy",
        service = "ExperionAgent",
        version = "2.0.0",
        timestamp = DateTime.UtcNow
    });
}
