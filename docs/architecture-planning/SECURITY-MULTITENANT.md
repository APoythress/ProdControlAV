# Security & Multi-Tenant - Architecture Planning Guide

## Overview

This document guides coding agents through security and multi-tenant considerations when making changes to ProdControlAV. Security and multi-tenancy are **first-class concerns** and must be considered in every change.

## Core Security Principles

### 1. Defense in Depth
- **Multiple security layers**: Authentication, authorization, input validation, encryption
- **Fail secure**: Default to deny access, require explicit allow
- **Principle of least privilege**: Minimum necessary permissions
- **Audit everything**: Log all security-relevant events

### 2. Multi-Tenant Isolation
- **Data segregation**: Every query filtered by TenantId
- **API isolation**: Middleware validates tenant access
- **Storage isolation**: Partition keys use TenantId
- **Network isolation**: No cross-tenant communication

### 3. Secure by Default
- **HTTPS only**: All communication encrypted
- **Strong authentication**: JWT tokens, API keys, cookies
- **Input validation**: Validate and sanitize all input
- **Output encoding**: Prevent injection attacks

## Multi-Tenant Architecture

### Tenant Isolation Model

```
┌─────────────────────────────────────────────────────────────┐
│                    ProdControlAV System                      │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  Tenant A                  │  Tenant B                       │
│  ┌──────────────┐         │  ┌──────────────┐              │
│  │ Users        │         │  │ Users        │              │
│  │ Devices      │         │  │ Devices      │              │
│  │ Data         │         │  │ Data         │              │
│  └──────────────┘         │  └──────────────┘              │
│         │                  │         │                       │
│         ▼                  │         ▼                       │
│  ┌──────────────┐         │  ┌──────────────┐              │
│  │ SQL Database │         │  │ SQL Database │              │
│  │ TenantId=A   │         │  │ TenantId=B   │              │
│  └──────────────┘         │  └──────────────┘              │
│         │                  │         │                       │
│         ▼                  │         ▼                       │
│  ┌──────────────┐         │  ┌──────────────┐              │
│  │Table Storage │         │  │Table Storage │              │
│  │Partition=A   │         │  │Partition=B   │              │
│  └──────────────┘         │  └──────────────┘              │
│                                                               │
└─────────────────────────────────────────────────────────────┘
```

### Tenant Context Management

Every request must establish tenant context:

```csharp
// Middleware to set tenant context
public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    
    public async Task InvokeAsync(HttpContext context, ITenantProvider tenantProvider)
    {
        // Get tenant from claims (cookie auth or JWT)
        var tenantClaim = context.User.FindFirst("tenant_id") ?? context.User.FindFirst("tenantId");
        
        if (tenantClaim != null && Guid.TryParse(tenantClaim.Value, out var tenantId))
        {
            tenantProvider.TenantId = tenantId;
        }
        else if (context.Request.Path.StartsWithSegments("/api"))
        {
            // API requires tenant context
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Tenant context required");
            return;
        }
        
        await _next(context);
    }
}

// Tenant provider implementation
public interface ITenantProvider
{
    Guid TenantId { get; set; }
}

public class TenantProvider : ITenantProvider
{
    public Guid TenantId { get; set; }
}
```

### Database Query Filtering

**Every database query MUST filter by TenantId:**

```csharp
// ✅ GOOD - Always filter by tenant
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    
    public async Task<ActionResult<List<Device>>> GetDevices()
    {
        var devices = await _db.Devices
            .Where(d => d.TenantId == _tenant.TenantId)
            .ToListAsync();
        
        return Ok(devices);
    }
    
    public async Task<ActionResult<Device>> GetDevice(Guid id)
    {
        var device = await _db.Devices
            .Where(d => d.Id == id && d.TenantId == _tenant.TenantId)
            .FirstOrDefaultAsync();
        
        if (device == null)
            return NotFound();
        
        return Ok(device);
    }
}

// ❌ BAD - Missing tenant filter (security vulnerability)
var devices = await _db.Devices.ToListAsync(); // Exposes all tenant data!
```

### Table Storage Multi-Tenancy

Use TenantId as PartitionKey for isolation:

```csharp
public class TableDeviceStore : IDeviceStore
{
    private string GetPartitionKey(Guid tenantId) => tenantId.ToString();
    private string GetRowKey(Guid deviceId) => deviceId.ToString();
    
    public async IAsyncEnumerable<DeviceDto> GetAllForTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var table = _tableServiceClient.GetTableClient("Devices");
        
        // Partition key filter ensures tenant isolation
        var filter = $"PartitionKey eq '{GetPartitionKey(tenantId)}'";
        
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter, cancellationToken: ct))
        {
            yield return MapToDto(entity);
        }
    }
    
    // ❌ BAD - Scanning entire table (expensive and insecure)
    // await foreach (var entity in table.QueryAsync<TableEntity>(ct))
}
```

### Queue Storage Multi-Tenancy

Include TenantId in queue names:

```csharp
// Queue naming: pcav-{tenantId}-{agentId}
private static string GetQueueName(Guid tenantId, Guid agentId) 
    => $"pcav-{tenantId}-{agentId}".ToLowerInvariant();

public async Task EnqueueCommandAsync(Guid tenantId, Guid agentId, AgentCommand command, CancellationToken ct)
{
    // Tenant-specific queue
    var queueName = GetQueueName(tenantId, agentId);
    var queue = _queueServiceClient.GetQueueClient(queueName);
    
    await queue.CreateIfNotExistsAsync(cancellationToken: ct);
    await queue.SendMessageAsync(messageJson, cancellationToken: ct);
}
```

## Authentication Mechanisms

### 1. Cookie Authentication (Web Users)

```csharp
// In Program.cs
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/signin";
        options.LogoutPath = "/signout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
    });

// Login implementation
public async Task<IActionResult> Login(LoginRequest request)
{
    // Validate credentials
    var user = await _db.AppUsers
        .Include(u => u.UserTenants)
        .FirstOrDefaultAsync(u => u.Email == request.Email);
    
    if (user == null || !VerifyPassword(request.Password, user.PasswordHash))
        return Unauthorized();
    
    // Create claims
    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim("tenant_id", user.UserTenants.First().TenantId.ToString())
    };
    
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);
    
    await HttpContext.SignInAsync(principal);
    
    return Ok();
}
```

### 2. JWT Authentication (Agents)

```csharp
// In Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };
    });

// Token generation
public async Task<IActionResult> AuthenticateAgent(AgentAuthRequest request)
{
    // Validate agent key
    var agent = await _db.Agents
        .FirstOrDefaultAsync(a => a.ApiKey == request.AgentKey);
    
    if (agent == null)
        return Unauthorized();
    
    // Generate JWT token
    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, agent.Id.ToString()),
        new Claim("tenantId", agent.TenantId.ToString()),
        new Claim("agentName", agent.Name),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
    };
    
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
    var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    
    var token = new JwtSecurityToken(
        issuer: _jwtOptions.Issuer,
        audience: _jwtOptions.Audience,
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes),
        signingCredentials: credentials
    );
    
    var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
    
    return Ok(new
    {
        token = tokenString,
        expiresAt = token.ValidTo,
        tokenType = "Bearer"
    });
}
```

### 3. API Key Authentication (Legacy/Simple)

```csharp
// Agent API key validation
public class ApiKeyAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly AppDbContext _db;
    
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!context.HttpContext.Request.Headers.TryGetValue("X-Api-Key", out var apiKey))
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.ApiKey == apiKey.ToString());
        
        if (agent == null)
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        
        // Set tenant context
        var tenantProvider = context.HttpContext.RequestServices.GetRequiredService<ITenantProvider>();
        tenantProvider.TenantId = agent.TenantId;
    }
}
```

## Authorization Policies

### Role-Based Access Control

```csharp
// Define roles
public static class Roles
{
    public const string Admin = "Admin";
    public const string Member = "Member";
    public const string Viewer = "Viewer";
}

// Register policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("IsAdmin", policy => 
        policy.RequireClaim("role", Roles.Admin));
    
    options.AddPolicy("IsMember", policy => 
        policy.RequireClaim("role", Roles.Admin, Roles.Member));
    
    options.AddPolicy("TenantMember", policy => 
        policy.RequireAssertion(context => 
            context.User.HasClaim(c => c.Type == "tenant_id" || c.Type == "tenantId")));
});

// Use in controllers
[Authorize(Policy = "IsAdmin")]
public async Task<IActionResult> DeleteDevice(Guid id)
{
    // Only admins can delete devices
}

[Authorize(Policy = "IsMember")]
public async Task<IActionResult> CreateDevice(CreateDeviceRequest request)
{
    // Admins and members can create devices
}

[Authorize(Policy = "TenantMember")]
public async Task<IActionResult> GetDevices()
{
    // Any authenticated tenant member can view devices
}
```

## Input Validation

### Request Validation

```csharp
public class CreateDeviceRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; set; } = "";
    
    [Required]
    [RegularExpression(@"^(?:[0-9]{1,3}\.){3}[0-9]{1,3}$|^[a-zA-Z0-9.-]+$")]
    public string Ip { get; set; } = "";
    
    [Range(1, 65535)]
    public int Port { get; set; } = 80;
    
    [StringLength(500)]
    public string? Location { get; set; }
}

// In controller
[HttpPost]
public async Task<IActionResult> CreateDevice([FromBody] CreateDeviceRequest request)
{
    if (!ModelState.IsValid)
        return BadRequest(ModelState);
    
    // Additional business validation
    if (await _db.Devices.AnyAsync(d => d.Ip == request.Ip && d.TenantId == _tenant.TenantId))
        return BadRequest("Device with this IP already exists");
    
    // Create device...
}
```

### SQL Injection Prevention

Entity Framework Core automatically parameterizes queries:

```csharp
// ✅ SAFE - EF Core parameterizes
var devices = await _db.Devices
    .Where(d => d.Name.Contains(searchTerm))
    .ToListAsync();

// ❌ DANGEROUS - Never use raw SQL with string concatenation
// var query = $"SELECT * FROM Devices WHERE Name LIKE '%{searchTerm}%'";
// var devices = await _db.Devices.FromSqlRaw(query).ToListAsync();

// ✅ SAFE - Use FromSqlInterpolated for raw SQL
var devices = await _db.Devices
    .FromSqlInterpolated($"SELECT * FROM Devices WHERE Name LIKE '%' + {searchTerm} + '%'")
    .ToListAsync();
```

### XSS Prevention (Blazor)

Blazor automatically encodes output:

```razor
@* ✅ SAFE - Blazor encodes by default *@
<div>@device.Name</div>

@* ⚠️ UNSAFE - Raw HTML (only use for trusted content) *@
<div>@((MarkupString)device.Description)</div>
```

## Secrets Management

### Azure Key Vault (Production)

```csharp
// In Program.cs
if (builder.Environment.IsProduction())
{
    var keyVaultEndpoint = new Uri(builder.Configuration["KeyVault:Endpoint"]);
    builder.Configuration.AddAzureKeyVault(keyVaultEndpoint, new DefaultAzureCredential());
}

// Access secrets
var connectionString = builder.Configuration["ConnectionStrings:DefaultConnection"];
var jwtKey = builder.Configuration["Jwt:Key"];
```

### Local Secrets (Development)

```bash
# Initialize user secrets
dotnet user-secrets init --project src/ProdControlAV.API

# Set secrets
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=..." --project src/ProdControlAV.API
dotnet user-secrets set "Jwt:Key" "your-secret-key-here" --project src/ProdControlAV.API
```

### Environment Variables

```bash
# Set environment variables
export ConnectionStrings__DefaultConnection="Server=..."
export Jwt__Key="your-secret-key-here"
```

### Never Commit Secrets

```gitignore
# .gitignore
appsettings.Development.json
appsettings.Production.json
*.user
*.secrets.json
```

## Data Encryption

### Encryption at Rest

- **Azure SQL Database**: Transparent Data Encryption (TDE) enabled by default
- **Azure Table Storage**: Encrypted at rest automatically
- **Azure Queue Storage**: Encrypted at rest automatically

### Encryption in Transit

- **HTTPS only**: All communication uses TLS 1.2+
- **HSTS**: HTTP Strict Transport Security enabled

```csharp
// In Program.cs
app.UseHsts(); // Enable HSTS in production
app.UseHttpsRedirection(); // Redirect HTTP to HTTPS

// Configure Kestrel for HTTPS
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureHttpsDefaults(httpsOptions =>
    {
        httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
    });
});
```

### Sensitive Data Handling

```csharp
// ❌ BAD - Logging sensitive data
_logger.LogInformation("User logged in with password {Password}", password);

// ✅ GOOD - Never log sensitive data
_logger.LogInformation("User {Email} logged in successfully", user.Email);

// ❌ BAD - Storing plain text passwords
user.Password = request.Password;

// ✅ GOOD - Hash passwords
user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
```

## Audit Logging

### Security Events to Log

```csharp
public class SecurityAuditLogger
{
    private readonly ILogger<SecurityAuditLogger> _logger;
    
    public void LogLoginAttempt(string email, bool success, string ipAddress)
    {
        if (success)
        {
            _logger.LogInformation(
                "User {Email} logged in successfully from {IpAddress}",
                email, ipAddress);
        }
        else
        {
            _logger.LogWarning(
                "Failed login attempt for {Email} from {IpAddress}",
                email, ipAddress);
        }
    }
    
    public void LogDataAccess(Guid userId, Guid tenantId, string resource, string action)
    {
        _logger.LogInformation(
            "User {UserId} in tenant {TenantId} performed {Action} on {Resource}",
            userId, tenantId, action, resource);
    }
    
    public void LogPermissionDenied(Guid userId, string resource, string action)
    {
        _logger.LogWarning(
            "Permission denied: User {UserId} attempted {Action} on {Resource}",
            userId, action, resource);
    }
}
```

### Database Audit Trail

```csharp
public class AuditEntry
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public string IpAddress { get; set; } = "";
}

// Automatically create audit entries
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var auditEntries = CreateAuditEntries();
    var result = await base.SaveChangesAsync(ct);
    
    foreach (var auditEntry in auditEntries)
    {
        AuditEntries.Add(auditEntry);
    }
    
    await base.SaveChangesAsync(ct);
    return result;
}
```

## Security Testing

### Security Test Checklist

- [ ] **Authentication bypass**: Try accessing endpoints without authentication
- [ ] **Authorization bypass**: Try accessing resources from different tenant
- [ ] **SQL injection**: Test with `'; DROP TABLE Devices; --`
- [ ] **XSS**: Test with `<script>alert('XSS')</script>`
- [ ] **CSRF**: Test POST without anti-forgery token
- [ ] **Insecure direct object reference**: Try accessing other tenant's resources by ID
- [ ] **Mass assignment**: Try setting fields that shouldn't be settable
- [ ] **JWT tampering**: Modify token and try to use it

### Security Test Example

```csharp
[Fact]
public async Task GetDevice_FromDifferentTenant_Returns404()
{
    // Arrange
    var tenant1Id = Guid.NewGuid();
    var tenant2Id = Guid.NewGuid();
    
    var device = new Device 
    { 
        Id = Guid.NewGuid(), 
        TenantId = tenant1Id, 
        Name = "Device1" 
    };
    _db.Devices.Add(device);
    await _db.SaveChangesAsync();
    
    // Act as tenant 2
    _mockTenantProvider.Setup(t => t.TenantId).Returns(tenant2Id);
    
    var result = await _controller.GetDevice(device.Id);
    
    // Assert
    Assert.IsType<NotFoundResult>(result.Result);
}

[Fact]
public async Task CreateDevice_WithoutAuthentication_Returns401()
{
    // Arrange
    var controller = new DevicesController(_db, _tenant);
    controller.ControllerContext = new ControllerContext
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal() // No claims = not authenticated
        }
    };
    
    // Act
    var result = await controller.CreateDevice(new CreateDeviceRequest());
    
    // Assert
    Assert.IsType<UnauthorizedResult>(result.Result);
}
```

## Common Security Pitfalls

### ❌ Avoid These Anti-Patterns

1. **Missing tenant validation**
   ```csharp
   // ❌ BAD - No tenant check
   var device = await _db.Devices.FindAsync(id);
   return Ok(device);
   ```
   
   **Fix**: Always validate tenant
   ```csharp
   // ✅ GOOD
   var device = await _db.Devices.FindAsync(id);
   if (device == null || device.TenantId != _tenant.TenantId)
       return NotFound();
   return Ok(device);
   ```

2. **Logging sensitive data**
   ```csharp
   // ❌ BAD
   _logger.LogInformation("API key: {ApiKey}", apiKey);
   ```
   
   **Fix**: Never log secrets
   ```csharp
   // ✅ GOOD
   _logger.LogInformation("API key validated for agent {AgentId}", agentId);
   ```

3. **Using HTTP instead of HTTPS**
   ```csharp
   // ❌ BAD
   var client = new HttpClient { BaseAddress = new Uri("http://api.example.com") };
   ```
   
   **Fix**: Always use HTTPS
   ```csharp
   // ✅ GOOD
   var client = new HttpClient { BaseAddress = new Uri("https://api.example.com") };
   ```

4. **Weak JWT secrets**
   ```csharp
   // ❌ BAD - Too short, predictable
   "Jwt": {
     "Key": "secret"
   }
   ```
   
   **Fix**: Strong random key
   ```csharp
   // ✅ GOOD - 256+ bits
   "Jwt": {
     "Key": "your-random-256-bit-key-here-minimum-32-characters-long-please"
   }
   ```

5. **Not validating input**
   ```csharp
   // ❌ BAD - No validation
   device.Name = request.Name;
   ```
   
   **Fix**: Validate all input
   ```csharp
   // ✅ GOOD
   if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 200)
       return BadRequest("Invalid device name");
   device.Name = request.Name.Trim();
   ```

## Compliance Considerations

### GDPR Compliance

- **Data minimization**: Only collect necessary data
- **Right to erasure**: Implement user deletion
- **Data portability**: Allow data export
- **Consent management**: Track user consent
- **Data breach notification**: Have incident response plan

### Data Retention

```csharp
// Retention policy configuration
public class DataRetentionOptions
{
    public int DeviceStatusDays { get; set; } = 30;
    public int AuditLogDays { get; set; } = 365;
    public int CommandHistoryDays { get; set; } = 90;
}

// Cleanup background service
public class DataRetentionService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CleanupOldDataAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
        }
    }
    
    private async Task CleanupOldDataAsync(CancellationToken ct)
    {
        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-_options.DeviceStatusDays);
        
        // Delete old device status records
        await _db.DeviceStatusLogs
            .Where(s => s.Timestamp < cutoffDate)
            .ExecuteDeleteAsync(ct);
        
        _logger.LogInformation("Deleted device status records older than {CutoffDate}", cutoffDate);
    }
}
```

## Security Checklist

Before deploying changes with security implications:

- [ ] Authentication required for all endpoints
- [ ] Authorization checks implemented
- [ ] Tenant isolation verified
- [ ] Input validation implemented
- [ ] SQL injection prevented (parameterized queries)
- [ ] XSS prevented (output encoding)
- [ ] CSRF protection enabled (anti-forgery tokens)
- [ ] Secrets stored securely (Key Vault, not in code)
- [ ] Sensitive data not logged
- [ ] HTTPS enforced
- [ ] JWT secrets strong and rotated
- [ ] Audit logging implemented
- [ ] Security tests written and passing
- [ ] Penetration testing performed
- [ ] Code review completed

## References

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [JWT Best Practices](../JWT_AUTHENTICATION.md)
- [Azure Security Best Practices](https://docs.microsoft.com/en-us/azure/security/fundamentals/best-practices-and-patterns)
- [.NET Security Guidelines](https://docs.microsoft.com/en-us/aspnet/core/security/)
