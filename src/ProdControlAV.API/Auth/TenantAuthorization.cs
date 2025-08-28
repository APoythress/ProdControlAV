// Policy marker

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

public sealed class TenantMemberRequirement : IAuthorizationRequirement {}

// Handler: validate only, do NOT select/override tenant
public sealed class TenantMemberHandler : AuthorizationHandler<TenantMemberRequirement>
{
    private readonly AppDbContext _db;
    public TenantMemberHandler(AppDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, TenantMemberRequirement requirement)
    {
        var userIdStr = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? context.User.FindFirstValue("sub");
        var tidValue  = context.User.FindFirstValue("tenant_id");

        if (!Guid.TryParse(userIdStr, out var userId)) return; // not authenticated
        if (!Guid.TryParse(tidValue, out var tenantId) || tenantId == Guid.Empty) return; // no active tenant

        var isMember = await _db.UserTenants
            .AsNoTracking()
            .AnyAsync(ut => ut.UserId == userId && ut.TenantId == tenantId);

        if (isMember)
            context.Succeed(requirement);
    }
}

