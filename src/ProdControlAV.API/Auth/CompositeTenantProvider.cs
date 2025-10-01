using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ProdControlAV.Core.Interfaces;

namespace ProdControlAV.API.Auth
{
    public sealed class CompositeTenantProvider : ITenantProvider
    {
        private readonly IHttpContextAccessor _http;
        public CompositeTenantProvider(IHttpContextAccessor http) => _http = http;

        public Guid TenantId
        {
            get
            {
                var ctx = _http.HttpContext;
                if (ctx is null) return Guid.Empty;

                // 1) Agent header
                if (ctx.Request.Headers.TryGetValue("X-Tenant", out var h) &&
                    Guid.TryParse(h.ToString(), out var fromHeader))
                {
                    // If an authenticated user is present, header must match their memberships
                    var user = ctx.User;
                    if (user?.Identity?.IsAuthenticated == true)
                    {
                        var memberships = user.FindFirst("tenant_ids")?.Value ?? string.Empty;
                        var set = memberships.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (!set.Contains(fromHeader.ToString()))
                            return Guid.Empty; // reject spoofed header
                    }
                    return fromHeader;
                }

                // 2) Claims for web users (cookie auth uses "tenant_id", JWT auth uses "tenantId")
                var claim = ctx.User?.FindFirstValue("tenant_id") 
                            ?? ctx.User?.FindFirstValue("tenantId");
                return Guid.TryParse(claim, out var fromClaim) ? fromClaim : Guid.Empty;
            }
        }
    }
}