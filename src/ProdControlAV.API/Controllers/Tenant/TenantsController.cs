using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ProdControlAV.API.Models;
using ProdControlAV.Core.Models;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("api/tenants")]
[Authorize(Policy = "TenantMember")]
public class TenantsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IOptions<ClientNotesOptions> _notesOptions;

    public TenantsController(AppDbContext db, IOptions<ClientNotesOptions> notesOptions)
    {
        _db = db;
        _notesOptions = notesOptions;
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

    /// <summary>
    /// Get tenant management details including agents and recent notes
    /// </summary>
    [Authorize]
    [HttpGet("{id}/manage")]
    public async Task<ActionResult<TenantManagementDto>> GetManagementDetails(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants
            .Include(t => t.TenantStatus)
            .Include(t => t.SubscriptionPlan)
            .FirstOrDefaultAsync(t => t.TenantId == id, ct);

        if (tenant is null)
            return NotFound(new { error = "tenant_not_found" });

        var agents = await _db.Agents
            .Where(a => a.TenantId == id)
            .Select(a => new AgentDto(a.Id, a.Name, a.LocationName, a.LastSeenUtc, a.Version))
            .ToListAsync(ct);

        var recentNotes = await _db.ClientNotes
            .Where(n => n.TenantId == id)
            .OrderByDescending(n => n.CreatedUtc)
            .Take(3)
            .Select(n => new ClientNoteDto(n.Id, n.NoteText, n.CreatedUtc, n.CreatedBy))
            .ToListAsync(ct);

        var result = new TenantManagementDto(
            tenant.TenantId,
            tenant.Name,
            tenant.Slug,
            tenant.TenantStatusId,
            tenant.TenantStatus?.TenantStatusText,
            tenant.SubscriptionPlanId,
            tenant.SubscriptionPlan?.SubscriptionPlanText,
            tenant.CreatedUtc,
            agents,
            recentNotes
        );

        return Ok(result);
    }

    /// <summary>
    /// Update tenant name
    /// </summary>
    [Authorize]
    [HttpPut("{id}/name")]
    public async Task<IActionResult> UpdateName(Guid id, [FromBody] UpdateTenantNameRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FindAsync(new object?[] { id }, ct);
        if (tenant is null)
            return NotFound(new { error = "tenant_not_found" });

        tenant.Name = request.Name.Trim();
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "name_updated" });
    }

    /// <summary>
    /// Update tenant subscription plan
    /// </summary>
    [Authorize]
    [HttpPut("{id}/subscription")]
    public async Task<IActionResult> UpdateSubscription(Guid id, [FromBody] UpdateTenantSubscriptionRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FindAsync(new object?[] { id }, ct);
        if (tenant is null)
            return NotFound(new { error = "tenant_not_found" });

        var subscriptionExists = await _db.TenantSubscriptionPlans
            .AnyAsync(sp => sp.SubscriptionPlanId == request.SubscriptionPlanId, ct);

        if (!subscriptionExists)
            return BadRequest(new { error = "invalid_subscription_plan" });

        tenant.SubscriptionPlanId = request.SubscriptionPlanId;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "subscription_updated" });
    }

    /// <summary>
    /// Update tenant status (with validation)
    /// </summary>
    [Authorize]
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateTenantStatusRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FindAsync(new object?[] { id }, ct);
        if (tenant is null)
            return NotFound(new { error = "tenant_not_found" });

        var statusExists = await _db.TenantStatuses
            .AnyAsync(s => s.TenantStatusId == request.TenantStatusId, ct);

        if (!statusExists)
            return BadRequest(new { error = "invalid_status" });

        tenant.TenantStatusId = request.TenantStatusId;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "status_updated" });
    }

    /// <summary>
    /// Regenerate tenant slug (destructive operation)
    /// </summary>
    [Authorize]
    [HttpPost("{id}/regenerate-slug")]
    public async Task<ActionResult<RegenerateSlugResponse>> RegenerateSlug(Guid id, CancellationToken ct)
    {
        const int SlugLength = 16;
        const int MaxAttempts = 10;
        
        var tenant = await _db.Tenants.FindAsync(new object?[] { id }, ct);
        if (tenant is null)
            return NotFound(new { error = "tenant_not_found" });

        var newSlug = Guid.NewGuid().ToString("N")[..SlugLength];
        
        var attempts = 0;
        while (await _db.Tenants.AnyAsync(t => t.Slug == newSlug, ct) && attempts < MaxAttempts)
        {
            newSlug = Guid.NewGuid().ToString("N")[..SlugLength];
            attempts++;
        }

        if (attempts >= MaxAttempts)
            return StatusCode(500, new { error = "failed_to_generate_unique_slug" });

        tenant.Slug = newSlug;
        await _db.SaveChangesAsync(ct);

        return Ok(new RegenerateSlugResponse(newSlug));
    }

    /// <summary>
    /// Add a new agent for the tenant
    /// </summary>
    [Authorize]
    [HttpPost("{id}/agents")]
    public async Task<ActionResult<AgentDto>> AddAgent(Guid id, [FromBody] AddAgentRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FindAsync(new object?[] { id }, ct);
        if (tenant is null)
            return NotFound(new { error = "tenant_not_found" });

        var agentKey = Guid.NewGuid().ToString("N");
        var agentKeyHash = BCrypt.Net.BCrypt.HashPassword(agentKey);

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            TenantId = id,
            Name = request.Name.Trim(),
            LocationName = request.LocationName?.Trim(),
            AgentKeyHash = agentKeyHash
        };

        _db.Agents.Add(agent);
        await _db.SaveChangesAsync(ct);

        var agentDto = new AgentDto(agent.Id, agent.Name, agent.LocationName, agent.LastSeenUtc, agent.Version);
        return CreatedAtAction(nameof(GetAgents), new { id }, agentDto);
    }

    /// <summary>
    /// Get all agents for a tenant
    /// </summary>
    [Authorize]
    [HttpGet("{id}/agents")]
    public async Task<ActionResult<List<AgentDto>>> GetAgents(Guid id, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FindAsync(new object?[] { id }, ct);
        if (tenant is null)
            return NotFound(new { error = "tenant_not_found" });

        var agents = await _db.Agents
            .Where(a => a.TenantId == id)
            .Select(a => new AgentDto(a.Id, a.Name, a.LocationName, a.LastSeenUtc, a.Version))
            .ToListAsync(ct);

        return Ok(agents);
    }

    /// <summary>
    /// Add a note for the client/tenant
    /// </summary>
    [Authorize]
    [HttpPost("{id}/notes")]
    public async Task<ActionResult<ClientNoteDto>> AddNote(Guid id, [FromBody] AddClientNoteRequest request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FindAsync(new object?[] { id }, ct);
        if (tenant is null)
            return NotFound(new { error = "tenant_not_found" });

        if (request.NoteText.Length > _notesOptions.Value.MaxCharacterLimit)
            return BadRequest(new { error = "note_exceeds_character_limit", limit = _notesOptions.Value.MaxCharacterLimit });

        var userEmail = User.FindFirstValue(ClaimTypes.Email) ?? "Unknown";

        var note = new ClientNote
        {
            Id = Guid.NewGuid(),
            TenantId = id,
            NoteText = request.NoteText.Trim(),
            CreatedUtc = DateTime.UtcNow,
            CreatedBy = userEmail
        };

        _db.ClientNotes.Add(note);
        await _db.SaveChangesAsync(ct);

        var noteDto = new ClientNoteDto(note.Id, note.NoteText, note.CreatedUtc, note.CreatedBy);
        return CreatedAtAction(nameof(GetNotes), new { id, page = 1, pageSize = 15 }, noteDto);
    }

    /// <summary>
    /// Get notes for a tenant (paginated)
    /// </summary>
    [Authorize]
    [HttpGet("{id}/notes")]
    public async Task<ActionResult<ClientNotesResponse>> GetNotes(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 15, CancellationToken ct = default)
    {
        const int MinPage = 1;
        const int MinPageSize = 1;
        const int MaxPageSize = 100;
        const int DefaultPageSize = 15;
        
        var tenant = await _db.Tenants.FindAsync(new object?[] { id }, ct);
        if (tenant is null)
            return NotFound(new { error = "tenant_not_found" });

        if (page < MinPage) page = MinPage;
        if (pageSize < MinPageSize || pageSize > MaxPageSize) pageSize = DefaultPageSize;

        var totalCount = await _db.ClientNotes
            .Where(n => n.TenantId == id)
            .CountAsync(ct);

        var notes = await _db.ClientNotes
            .Where(n => n.TenantId == id)
            .OrderByDescending(n => n.CreatedUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new ClientNoteDto(n.Id, n.NoteText, n.CreatedUtc, n.CreatedBy))
            .ToListAsync(ct);

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var response = new ClientNotesResponse(notes, totalCount, pageSize, page, totalPages);
        return Ok(response);
    }

    /// <summary>
    /// Get list of available subscription plans
    /// </summary>
    [Authorize]
    [HttpGet("subscription-plans")]
    public async Task<ActionResult<List<TenantSubscriptionPlan>>> GetSubscriptionPlans(CancellationToken ct)
    {
        var plans = await _db.TenantSubscriptionPlans.ToListAsync(ct);
        return Ok(plans);
    }

    /// <summary>
    /// Get list of available tenant statuses
    /// </summary>
    [Authorize]
    [HttpGet("statuses")]
    public async Task<ActionResult<List<TenantStatus>>> GetStatuses(CancellationToken ct)
    {
        var statuses = await _db.TenantStatuses.ToListAsync(ct);
        return Ok(statuses);
    }
}
