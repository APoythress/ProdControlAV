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

    public record TenantWithStats
    {
        public Guid TenantId { get; set; }
        public string Name { get; set; } = default!;
        public string Slug { get; set; } = default!;
        public DateTime CreatedUtc { get; set; }
        public int DeviceCount { get; set; }
        public string? Status { get; set; }
        public string? Subscription { get; set; }
    }

    [Authorize]
    [HttpGet("all")]
    public async Task<ActionResult<List<TenantWithStats>>> GetAll(CancellationToken ct)
    {
        // Get all tenants with their related data
        var tenants = await _db.Tenants
            .Include(t => t.TenantStatus)
            .Include(t => t.SubscriptionPlan)
            .ToListAsync(ct);

        // Get device counts for all tenants in a single query
        var deviceCounts = await _db.Devices
            .GroupBy(d => d.TenantId)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var deviceCountDict = deviceCounts.ToDictionary(dc => dc.TenantId, dc => dc.Count);

        var result = tenants.Select(tenant => new TenantWithStats
        {
            TenantId = tenant.TenantId,
            Name = tenant.Name,
            Slug = tenant.Slug,
            CreatedUtc = tenant.CreatedUtc,
            DeviceCount = deviceCountDict.TryGetValue(tenant.TenantId, out var count) ? count : 0,
            Status = tenant.TenantStatus?.TenantStatusText ?? "Active",
            Subscription = tenant.SubscriptionPlan?.SubscriptionPlanText ?? "N/A"
        }).ToList();

        return Ok(result);
    }

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
