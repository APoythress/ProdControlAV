using System;
using Microsoft.AspNetCore.Http;

public sealed class HeaderTenantProvider : ITenantProvider
{
    private readonly IHttpContextAccessor _http;
    public HeaderTenantProvider(IHttpContextAccessor http) => _http = http;
    public Guid TenantId =>
        _http.HttpContext?.Request?.Headers.TryGetValue("X-Tenant", out var v) == true 
            ? Guid.Parse(v.ToString()) : Guid.Empty;
}