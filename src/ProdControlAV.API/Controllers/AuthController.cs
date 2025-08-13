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

        [Required] public TenantMode TenantMode { get; set; } = TenantMode.Create; // FE can send 0/1
        public string? NewTenantName { get; set; } // if Create
        public string? JoinCode { get; set; }      // if Join (slug or invite code)
    }

    public class LoginRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string Password { get; set; } = string.Empty;
    }

    public record SwitchTenantRequest(string TenantId);

    // ===== Registration =====
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            return Conflict(new { error = "email_exists" });

        var hash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12);

        var user = new AppUser
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? email : req.DisplayName.Trim(),
            PasswordHash = hash
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        if (req.TenantMode == TenantMode.Create)
        {
            var name = string.IsNullOrWhiteSpace(req.NewTenantName) ? "Default Tenant" : req.NewTenantName.Trim();
            var baseSlug = string.Join("-", name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var slug = baseSlug;

            // Ensure unique slug
            var i = 1;
            while (await _db.Tenants.AnyAsync(t => t.Slug == slug, ct))
                slug = $"{baseSlug}-{i++}";

            var tenant = new Tenant { Name = name, Slug = slug };
            _db.Tenants.Add(tenant);

            _db.UserTenants.Add(new UserTenant
            {
                UserId = user.Id,
                TenantId = tenant.Id,
                Role = "Owner"
            });
        }
        else // Join
        {
            if (string.IsNullOrWhiteSpace(req.JoinCode))
                return BadRequest(new { error = "join_code_required" });

            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Slug == req.JoinCode.ToLowerInvariant(), ct);
            if (tenant is null)
                return BadRequest(new { error = "tenant_not_found" });

            _db.UserTenants.Add(new UserTenant
            {
                UserId = user.Id,
                TenantId = tenant.Id,
                Role = "Member"
            });
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { ok = true });
    }

    // ===== Login =====
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(u => u.Memberships)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null || string.IsNullOrEmpty(user.PasswordHash) || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { error = "invalid_credentials" });

        var memberships = user.Memberships.Select(m => m.TenantId).ToList();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email)
        };
        if (memberships.Any())
        {
            claims.Add(new("tenant_ids", string.Join(" ", memberships)));
            claims.Add(new("tenant_id", memberships[0]));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = true });

        return Ok(new { ok = true });
    }

    // ===== Logout =====
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    // ===== Switch active tenant =====
    [Authorize]
    [HttpPost("switch-tenant")]
    public async Task<IActionResult> SwitchTenant([FromBody] SwitchTenantRequest req, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var memberships = await _db.UserTenants
            .Where(x => x.UserId.ToString() == userId)
            .Select(x => x.TenantId)
            .ToListAsync(ct);

        if (!memberships.Contains(req.TenantId))
            return Forbid();

        var email = User.FindFirstValue(ClaimTypes.Email) ?? "";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Email, email),
            new("tenant_ids", string.Join(" ", memberships)),
            new("tenant_id", req.TenantId)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = true });
        return NoContent();
    }
}
