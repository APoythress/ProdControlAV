using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/security")]
[Authorize(Policy = "TenantMember")]
public class SecurityController : ControllerBase
{
    private readonly IAntiforgery _antiforgery;
    public SecurityController(IAntiforgery antiforgery) => _antiforgery = antiforgery;

    [HttpGet("antiforgery-token")]
    public IActionResult GetToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }
}