# Multi-Tenancy Implementation Guide

This document provides detailed technical information about the multi-tenant architecture in ProdControlAV.API.

## Overview

ProdControlAV implements a **shared database, shared schema** multi-tenancy pattern where:
- All tenants share the same database and table structure
- Data isolation is enforced through `TenantId` filtering at the application layer
- Row-level security ensures tenants can only access their own data

## Architecture Components

### 1. Tenant Resolution

Multiple strategies are used to determine the current tenant context:

#### ClaimsTenantProvider
Extracts tenant ID from authenticated user claims:
```csharp
public class ClaimsTenantProvider : ITenantProvider
{
    public Guid TenantId => Guid.Parse(_httpContextAccessor.HttpContext?
        .User.FindFirst("tenant_id")?.Value ?? Guid.Empty.ToString());
}
```

#### HeaderTenantProvider  
Reads tenant ID from HTTP headers (primarily for API key authentication):
```csharp
public class HeaderTenantProvider : ITenantProvider
{
    public Guid TenantId => Guid.Parse(_httpContextAccessor.HttpContext?
        .Request.Headers["X-Tenant-ID"].FirstOrDefault() ?? Guid.Empty.ToString());
}
```

#### CompositeTenantProvider
Combines multiple tenant resolution strategies with fallback logic:
```csharp
public class CompositeTenantProvider : ITenantProvider
{
    public Guid TenantId
    {
        get
        {
            // Try claims first (for web users)
            if (_claimsProvider.TenantId != Guid.Empty)
                return _claimsProvider.TenantId;
            
            // Fallback to header (for API key auth)
            return _headerProvider.TenantId;
        }
    }
}
```

### 2. Database Context Integration

The `AppDbContext` automatically filters all queries by tenant:

```csharp
public class AppDbContext : DbContext
{
    private readonly ITenantProvider _tenant;
    
    // Expose tenant ID as a property for EF parameterization
    protected Guid CurrentTenantId => _tenant.TenantId;
    
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Configure global query filters for all tenant-aware entities
        builder.Entity<Device>()
            .HasQueryFilter(d => d.TenantId == CurrentTenantId);
            
        builder.Entity<DeviceStatusLog>()
            .HasQueryFilter(d => d.TenantId == CurrentTenantId);
            
        // ... other entities
    }
}
```

### 3. Controller Authorization

Controllers use policy-based authorization to enforce tenant membership:

```csharp
[ApiController]
[Authorize(Policy = "IsMember")] // Ensures user is member of a tenant
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    // All operations automatically scoped to current tenant
    public async Task<List<Device>> GetDevices()
    {
        // This query is automatically filtered by TenantId
        return await _context.Devices.ToListAsync();
    }
}
```

## Authentication Integration

### Cookie Authentication (Web Users)
Web users authenticate and receive a cookie containing tenant membership claims:

```csharp
var claims = new List<Claim>
{
    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new Claim("tenant_id", userTenant.TenantId.ToString()),
    new Claim("tenant_role", userTenant.Role)
};

var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
await HttpContext.SignInAsync(new ClaimsPrincipal(identity));
```

### API Key Authentication (Agents)
Agents authenticate with API keys that are pre-registered to specific tenants:

```csharp
public class ApiKeyMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        var apiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
        
        if (!string.IsNullOrEmpty(apiKey))
        {
            var keyInfo = await _keyService.ValidateApiKeyAsync(apiKey);
            if (keyInfo != null)
            {
                // Set tenant context for API key
                context.Request.Headers["X-Tenant-ID"] = keyInfo.TenantId.ToString();
                
                // Create minimal identity for the agent
                var claims = new[] 
                {
                    new Claim("agent_id", keyInfo.AgentId.ToString()),
                    new Claim("tenant_id", keyInfo.TenantId.ToString())
                };
                context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
            }
        }
    }
}
```

## Data Isolation Patterns

### 1. Automatic Query Filtering
All LINQ queries are automatically filtered by tenant:

```csharp
// This query automatically includes: WHERE TenantId = @CurrentTenantId
var devices = await _context.Devices
    .Where(d => d.Status == true)
    .ToListAsync();
```

### 2. Insert Operations
New entities must have TenantId set from current context:

```csharp
public async Task<Device> CreateDevice(Device device)
{
    // Ensure tenant ID is set from current context
    device.TenantId = _tenant.TenantId;
    
    _context.Devices.Add(device);
    await _context.SaveChangesAsync();
    return device;
}
```

### 3. Cross-Tenant Prevention
Attempts to access cross-tenant data are automatically blocked:

```csharp
// This will return null even if device exists in different tenant
var device = await _context.Devices.FirstOrDefaultAsync(d => d.Id == someDeviceId);

// This will throw exception if trying to update cross-tenant data
var device = new Device { Id = crossTenantDeviceId, TenantId = differentTenantId };
_context.Update(device); // This will fail due to query filter
```

## Security Considerations

### 1. Tenant Isolation Validation
The global middleware validates that authenticated users have valid tenant access:

```csharp
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var tenantId = context.User.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "missing_tenant" });
            return;
        }
    }
    
    await next();
});
```

### 2. API Key Security
- API keys must be minimum 32 characters for security
- Keys are associated with specific tenants during registration
- Keys are validated on every request and can be revoked
- Key usage is logged for audit purposes

### 3. Database-Level Protection
Entity Framework's global query filters provide defense-in-depth:
- Even direct SQL queries through the context are filtered
- Prevents accidental cross-tenant data access
- Cannot be bypassed without explicit `IgnoreQueryFilters()`

## Configuration

### Dependency Injection Setup
```csharp
// Register tenant providers
builder.Services.AddScoped<ClaimsTenantProvider>();
builder.Services.AddScoped<HeaderTenantProvider>();
builder.Services.AddScoped<ITenantProvider, CompositeTenantProvider>();

// Register database context with tenant provider
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var tenantProvider = serviceProvider.GetRequiredService<ITenantProvider>();
    options.UseSqlServer(connectionString);
});
```

### Authorization Policies
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("IsMember", policy =>
        policy.Requirements.Add(new TenantMemberRequirement()));
});

builder.Services.AddScoped<IAuthorizationHandler, TenantMemberHandler>();
```

## Testing Multi-Tenancy

### Unit Testing
Mock the ITenantProvider to test different tenant contexts:

```csharp
[Test]
public async Task GetDevices_OnlyReturnsTenantDevices()
{
    // Arrange
    var tenantId = Guid.NewGuid();
    var mockTenantProvider = new Mock<ITenantProvider>();
    mockTenantProvider.Setup(x => x.TenantId).Returns(tenantId);
    
    // Act
    var devices = await controller.GetDevices();
    
    // Assert
    Assert.That(devices.All(d => d.TenantId == tenantId));
}
```

### Integration Testing
Test with multiple tenants to verify isolation:

```csharp
[Test]
public async Task CreateDevice_AutomaticallySetsTenantId()
{
    // Arrange - authenticate as tenant A
    await AuthenticateAsTenant(tenantAId);
    var device = new Device { Name = "Test Device" };
    
    // Act
    var created = await _client.PostAsJsonAsync("/api/devices", device);
    
    // Assert
    var result = await created.Content.ReadFromJsonAsync<Device>();
    Assert.That(result.TenantId, Is.EqualTo(tenantAId));
}
```

## Migration Considerations

When adding new tenant-aware entities:

1. **Add TenantId Property**:
```csharp
public class NewEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; } // Required for all tenant-aware entities
    // ... other properties
}
```

2. **Configure Query Filter**:
```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    builder.Entity<NewEntity>()
        .HasQueryFilter(e => e.TenantId == CurrentTenantId);
}
```

3. **Update Controllers**:
```csharp
[Authorize(Policy = "IsMember")] // Ensure tenant authorization
public class NewEntityController : ControllerBase
{
    // Controller automatically scoped to tenant
}
```

## Troubleshooting

### Common Issues

#### "Missing Tenant" Errors
**Symptom**: 401 responses with `{"error": "missing_tenant"}`
**Cause**: User authenticated but no tenant claim present
**Solution**: Check authentication logic sets tenant claim correctly

#### Cross-Tenant Data Access
**Symptom**: Users seeing data from other tenants
**Cause**: Query filter not configured for entity
**Solution**: Add `HasQueryFilter` configuration for all tenant-aware entities

#### API Key Authentication Failures
**Symptom**: 401 responses for agent requests
**Cause**: API key not properly configured or expired
**Solution**: Verify API key registration and tenant association

### Debugging Tools

#### Log Tenant Context
```csharp
public class TenantLoggingMiddleware
{
    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = context.User.FindFirst("tenant_id")?.Value;
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TenantId"] = tenantId ?? "None"
        });
        
        await _next(context);
    }
}
```

#### EF Query Logging
Enable sensitive data logging to see tenant filtering in SQL:
```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString)
           .EnableSensitiveDataLogging() // Only in development
           .LogTo(Console.WriteLine, LogLevel.Information);
});
```

This will show SQL queries with tenant filtering:
```sql
SELECT [d].[Id], [d].[Name], [d].[TenantId]
FROM [Devices] AS [d]
WHERE [d].[TenantId] = @__CurrentTenantId_0
```