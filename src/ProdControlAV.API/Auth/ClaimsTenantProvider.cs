using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProdControlAV.Core.Interfaces;

namespace ProdControlAV.API.Auth;

public sealed class ClaimsTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _accessor;

    public ClaimsTenantProvider(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    // Returns the active tenant from the authenticated user's claims; Guid.Empty if not set or invalid
    public Guid TenantId
    {
        get
        {
            var value = _accessor.HttpContext?.User?.FindFirstValue("tenant_id");
            return Guid.TryParse(value, out var id) ? id : Guid.Empty;
        }
    }
}
