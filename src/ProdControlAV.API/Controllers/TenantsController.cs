using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Core.Models;
// <-- add this

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize(Policy = "TenantMember")]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TenantsController(AppDbContext db)
    {
        _db = db;
    }

    public record CreateTenantRequest(string Name, string Slug);

    [Authorize]
    [HttpPost]
    public async Task<ActionResult<Tenant>> Create([FromBody] CreateTenantRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Slug))
            return BadRequest(new { error = "name_and_slug_required" });

        var slug = req.Slug.Trim().ToLowerInvariant();

        if (await _db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            return Conflict(new { error = "slug_exists" });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var userGuid = Guid.Parse(userId);

        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(), // ensure a real PK is persisted
            Name = req.Name.Trim(),
            Slug = slug
        };

        _db.Tenants.Add(tenant);
        _db.UserTenants.Add(new UserTenant
        {
            UserId = userGuid,
            TenantId = tenant.TenantId,
            Role = "Owner"
        });

        await _db.SaveChangesAsync(ct);

        // Re-issue cookie using the SAME scheme as login/switch-tenant
        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var memberships = await _db.UserTenants
            .Where(m => m.UserId == userGuid)
            .Select(m => m.TenantId)
            .ToListAsync(ct);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userGuid.ToString()),
            new(ClaimTypes.Email, email),
            new("tenant_ids", string.Join(" ", memberships)),
            new("tenant_id", tenant.TenantId.ToString())
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });

        return CreatedAtAction(nameof(Get), new { id = tenant.TenantId }, tenant);
    }

    [Authorize]
    [HttpGet("{id}")]
    public async Task<ActionResult<Tenant>> Get(string id, CancellationToken ct)
    {
        var t = await _db.Tenants.FindAsync(new object?[] { id }, ct);
        return t is null ? NotFound() : Ok(t);
    }
}
