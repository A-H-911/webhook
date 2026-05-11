using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hookbin.API.Controllers;

[ApiController]
[Route("api")]
public sealed class InfoController : ControllerBase
{
    [HttpGet("version")]
    [AllowAnonymous]
    public IActionResult GetVersion()
    {
        var version = typeof(InfoController).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        return Ok(new { version });
    }
}
