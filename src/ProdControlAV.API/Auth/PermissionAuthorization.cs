using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace ProdControlAV.API.Auth;

/// <summary>
/// Requirement for checking if a user has a specific permission
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission ?? throw new ArgumentNullException(nameof(permission));
    }
}

/// <summary>
/// Handler for permission-based authorization
/// </summary>
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly AppDbContext _db;

    public PermissionHandler(AppDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub");

        if (!Guid.TryParse(userIdStr, out var userId))
            return; // not authenticated

        // Check if user has DevAdmin role - if so, grant all permissions
        var userRoles = await _db.UserTenants
            .AsNoTracking()
            .Where(ut => ut.UserId == userId)
            .Select(ut => ut.Role)
            .ToListAsync();

        if (userRoles.Any(r => r == "DevAdmin"))
        {
            context.Succeed(requirement);
            return;
        }

        // Check if user has the specific permission
        var hasPermission = await _db.UserPermissions
            .AsNoTracking()
            .AnyAsync(up => up.UserId == userId && up.Permission == requirement.Permission);

        if (hasPermission)
            context.Succeed(requirement);
    }
}
