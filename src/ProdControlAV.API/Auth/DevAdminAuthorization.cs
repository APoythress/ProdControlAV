using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ProdControlAV.API.Auth;

/// <summary>
/// Authorization requirement for DevAdmin-only operations
/// DevAdmin has full access to all endpoints
/// </summary>
public sealed class DevAdminRequirement : IAuthorizationRequirement {}

/// <summary>
/// Handler to validate that a user has the "DevAdmin" role
/// </summary>
public sealed class DevAdminHandler : AuthorizationHandler<DevAdminRequirement>
{
    private readonly AppDbContext _db;
    
    public DevAdminHandler(AppDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, 
        DevAdminRequirement requirement)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdStr, out var userId)) return; // not authenticated

        // Check if user has DevAdmin role for any tenant (DevAdmin is global)
        var isDevAdmin = await _db.UserTenants
            .AsNoTracking()
            .AnyAsync(ut => ut.UserId == userId && ut.Role == "DevAdmin");

        if (isDevAdmin)
            context.Succeed(requirement);
    }
}
