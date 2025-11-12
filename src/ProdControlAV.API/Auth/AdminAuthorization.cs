using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ProdControlAV.API.Auth;

/// <summary>
/// Authorization requirement for admin-only operations
/// </summary>
public sealed class AdminRequirement : IAuthorizationRequirement {}

/// <summary>
/// Handler to validate that a user has the "Admin" role
/// </summary>
public sealed class AdminHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly AppDbContext _db;
    
    public AdminHandler(AppDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, 
        AdminRequirement requirement)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub");
        var tidValue = context.User.FindFirstValue("tenant_id");

        if (!Guid.TryParse(userIdStr, out var userId)) return; // not authenticated
        if (!Guid.TryParse(tidValue, out var tenantId) || tenantId == Guid.Empty) return; // no active tenant

        // Check if user has Admin or DevAdmin role
        // DevAdmin has global access, Admin is tenant-specific
        var userRoles = await _db.UserTenants
            .AsNoTracking()
            .Where(ut => ut.UserId == userId)
            .Select(ut => new { ut.Role, ut.TenantId })
            .ToListAsync();

        // DevAdmin has access to everything
        if (userRoles.Any(r => r.Role == "DevAdmin"))
        {
            context.Succeed(requirement);
            return;
        }

        // Admin has access to their tenant's resources
        if (userRoles.Any(r => r.Role == "Admin" && r.TenantId == tenantId))
        {
            context.Succeed(requirement);
        }
    }
}
