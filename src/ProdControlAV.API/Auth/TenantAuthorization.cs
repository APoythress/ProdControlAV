using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace ProdControlAV.API.Auth;

public sealed class TenantMemberRequirement : IAuthorizationRequirement { }

public sealed class TenantMemberHandler : AuthorizationHandler<TenantMemberRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantMemberRequirement requirement)
    {
        var currentTenant = context.User.FindFirst("tenant_id")?.Value;
        var memberships = context.User.FindFirst("tenant_ids")?.Value ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(currentTenant))
        {
            var set = memberships.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                 .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (set.Contains(currentTenant))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }
}
