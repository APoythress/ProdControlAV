using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BCrypt.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ProdControlAV.API.Models;

namespace ProdControlAV.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext db, ILogger<AuthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ===== DTOs =====
    public enum TenantMode { Create = 0, Join = 1 }

    public class RegisterRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required, MinLength(8)] public string Password { get; set; } = string.Empty;
        [Compare(nameof(Password))] public string ConfirmPassword { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        [Required, MinLength(8)] public string JoinCode { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Password { get; set; } = string.Empty;
        [Required] public Guid TenantId { get; set; } =  Guid.Empty;
    }

    // TenantId is now a Guid
    public record SwitchTenantRequest(Guid TenantId);

    // ===== Registration =====
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) 
            return ValidationProblem(ModelState);

        var email = req.Email.Trim().ToLowerInvariant();
        var hash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12);

        var tenant = await _db.Tenants
            .Where(t => t.Slug == req.JoinCode)
            .FirstOrDefaultAsync(ct);

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict(new { error = "email_exists" });

        if (string.IsNullOrWhiteSpace(req.JoinCode))
            return BadRequest(new { error = "join_code_required" });

        if (tenant is null)
            return BadRequest(new { error = "tenant_not_found" });

        var user = new AppUser
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? email : req.DisplayName.Trim(),
            PasswordHash = hash,
            TenantId = tenant.TenantId,
        };

        // Add and save user so the key is guaranteed
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        _db.UserTenants.Add(new UserTenant
        {
            UserId = user.UserId,
            TenantId = tenant.TenantId,
            Role = "Member"
        });

        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }


    // ===== Login =====
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var email = req.Email.Trim().ToLowerInvariant();

        // 1) Load only the user row; avoid relying on navigation mapping here
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            _logger.LogWarning("Login failed: user not found for {Email}", email);
            return Unauthorized(new { error = "invalid_credentials" });
        }

        // 2) Verify password explicitly
        bool passwordOk;
        try
        {
            passwordOk = !string.IsNullOrEmpty(user.PasswordHash) &&
                         BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed: password verification threw for {Email}", email);
            return Unauthorized(new { error = "invalid_credentials" });
        }

        if (!passwordOk)
        {
            _logger.LogWarning("Login failed: password mismatch for {Email}", email);
            return Unauthorized(new { error = "invalid_credentials" });
        }

        // 3) Load memberships via UserTenants keyed by UserId only
        var membershipList = await _db.UserTenants
            .Where(m => m.UserId == user.UserId)
            .Select(m => new { m.TenantId, m.Role })
            .Distinct()
            .ToListAsync(ct);

        if (membershipList.Count == 0)
        {
            _logger.LogWarning("Login failed: no tenant memberships for user {UserId}", user.UserId);
            return Unauthorized(new { error = "no_tenant_membership" });
        }
        
        // In your Login action, replace the membership selection and active-tenant choice with this:
        var userGuid = user.UserId;

        // Pull tenant IDs strictly from UserTenants WHERE UserId == userGuid
        var tenantIds = await _db.UserTenants
            .AsNoTracking()
            .Where(m => m.UserId == userGuid)
            .Select(m => m.TenantId)
            .Distinct()
            .OrderBy(id => id) // stable, deterministic order
            .ToListAsync(ct);

        if (tenantIds.Count == 0)
        {
            _logger.LogWarning("Login failed: no tenant memberships for user {UserId}", userGuid);
            return Unauthorized(new { error = "no_tenant_membership" });
        }

        // If the request specified a tenant, only accept it if it is in the membership set
        Guid activeTenant = tenantIds.First();
        if (req.TenantId != Guid.Empty && tenantIds.Contains(req.TenantId))
        {
            activeTenant = req.TenantId;
        }

        // Build claims ONLY from membership-derived tenantIds
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userGuid.ToString()),
            new(ClaimTypes.Email, user.Email),
            new("tenant_ids", string.Join(" ", tenantIds.Select(t => t.ToString()))),
            new("tenant_id", activeTenant.ToString())
        };

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });

        return Ok(new { ok = true, tenantId = activeTenant });
    }

    // ===== Switch active tenant =====
    [Authorize]
    [HttpPost("switch-tenant")]
    public async Task<IActionResult> SwitchTenant([FromBody] SwitchTenantRequest req, CancellationToken ct)
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized();

        var memberships = await _db.UserTenants
            .Where(x => x.UserId == userId)
            .Select(x => new { x.TenantId, x.Role })
            .ToListAsync(ct);

        if (!memberships.Any(m => m.TenantId == req.TenantId))
            return Forbid();

        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";

        var tenantIds = memberships.Select(m => m.TenantId).ToList();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new("tenant_ids", string.Join(" ", tenantIds.Select(id => id.ToString()))),
            new("tenant_id", req.TenantId.ToString())
        };

        foreach (var m in memberships)
        {
            claims.Add(new Claim("tenant_membership", $"{m.TenantId:D}:{m.Role}"));
        }

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });

        return NoContent();
    }

    // ===== Logout =====
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

}