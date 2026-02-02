using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ProdControlAV.API.Models;
using ProdControlAV.API.Services;
using ProdControlAV.Infrastructure.Services;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ProdControlAV.API.Auth;
using Microsoft.Extensions.Logging;
using Azure.Data.Tables;
using Azure.Identity;

var builder = WebApplication.CreateBuilder(args);

// Disable JWT claim type mapping to preserve original claim names from tokens
// This allows "sub" to remain "sub" instead of being mapped to ClaimTypes.NameIdentifier
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

// Controllers + JSON enum strings (optional)
builder.Services.AddControllers();

// Http + misc
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");

builder.Services.AddHttpClient("device-commands")
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = false
            // NOTE: keep default cert validation; you’re calling http:// IPs above.
        };
    });

// CORS for local dev same-origin (adjust origins as needed)
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevSpa", policy => policy
        .WithOrigins("https://localhost:5001", "http://localhost:5198") // your SPA hosts
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

// JWT Configuration
builder.Services.Configure<JwtConfig>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddScoped<IJwtService, JwtService>();

// Authentication - Cookie auth (for web users) and JWT Bearer (for agents)
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
    {
        o.LoginPath = "/signin"; // optional
        o.AccessDeniedPath = "/signin"; // Use signin page instead of default /Account/AccessDenied
        o.Cookie.Name = "prodcontrolav.auth";
        o.SlidingExpiration = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always; // Require HTTPS
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context =>
            {
                // For API requests, return 401 instead of redirecting to login page
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            },
            OnRedirectToAccessDenied = context =>
            {
                // For API requests, return 403 instead of redirecting
                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }
        };
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var jwtConfig = builder.Configuration.GetSection("Jwt").Get<JwtConfig>();
        if (jwtConfig != null && !string.IsNullOrEmpty(jwtConfig.Key))
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtConfig.Issuer,
                ValidAudience = jwtConfig.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.Key)),
                ClockSkew = TimeSpan.FromMinutes(1), // Allow 1 minute clock skew
                // Preserve original JWT claim names (sub, jti, etc.) instead of mapping to ClaimTypes
                NameClaimType = JwtRegisteredClaimNames.Sub,
                RoleClaimType = "role"
            };
        }
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetService<ILogger<ProdControlAV.API.Controllers.AgentsController>>();
                if (logger != null)
                {
                    logger.LogWarning("[JWT AUTH FAILED] {Exception} | Token: {Token}", context.Exception.ToString(), context.Request.Headers["Authorization"]);
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                var logger = context.HttpContext.RequestServices.GetService<ILogger<ProdControlAV.API.Controllers.AgentsController>>();
                if (logger != null)
                {
                    var claims = context.Principal?.Claims.Select(c => $"{c.Type}={c.Value}").ToList();
                    logger.LogInformation("[JWT TOKEN VALIDATED] Claims: {Claims}", string.Join(", ", claims ?? new List<string>()));
                }
                return Task.CompletedTask;
            }
        };
    });

// Authorization policy
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("MustHaveTenantId", policy =>
    {
        policy.RequireClaim("tenant_id");
    });
    
    options.AddPolicy("IsMember", policy =>
    {
        policy.RequireClaim("tenant_member", "member");
    });
    
    options.AddPolicy("TenantMember", policy =>
        policy.Requirements.Add(new TenantMemberRequirement()));
    
    // Admin policy - requires Admin or DevAdmin role in UserTenants
    options.AddPolicy("Admin", policy =>
        policy.Requirements.Add(new AdminRequirement()));
    
    // DevAdmin policy - requires DevAdmin role (global admin)
    options.AddPolicy("DevAdmin", policy =>
        policy.Requirements.Add(new DevAdminRequirement()));
    
    // JWT Agent policy - requires tenantId claim from JWT
    options.AddPolicy("JwtAgent", policy =>
    {
        policy.AuthenticationSchemes.Add(JwtBearerDefaults.AuthenticationScheme);
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("tenantId");
    });
    
    // ATEM Control policy - requires ATEM-Control permission
    options.AddPolicy("AtemControl", policy =>
        policy.Requirements.Add(new PermissionRequirement("ATEM-Control")));
});

// IMPORTANT: register the handler as Scoped (or Transient), not Singleton
builder.Services.AddScoped<IAuthorizationHandler, TenantMemberHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AdminHandler>();
builder.Services.AddScoped<IAuthorizationHandler, DevAdminHandler>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

// Device/infra services
builder.Services.AddSingleton<ICommandQueue>(new JsonCommandQueue("Data/Commands"));
builder.Services.AddScoped<IDeviceCommandService, DeviceCommandService>();
builder.Services.AddSingleton<IDeviceController>(new TelnetDeviceController());
builder.Services.AddSingleton<IDeviceStatusRepository, InMemoryDeviceStatusRepository>();
builder.Services.AddSingleton<INetworkMonitor, PingNetworkMonitor>();
builder.Services.AddScoped<ITenantProvider, CompositeTenantProvider>();
builder.Services.AddScoped<IAgentAuth, AgentAuth>();

// Azure Queue Storage for agent commands
builder.Services.AddScoped<IAgentCommandQueueService, AzureQueueAgentCommandService>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Azure Table Storage clients for device status
builder.Services.AddSingleton<TableServiceClient>(sp => {
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["Storage:TablesEndpoint"];
    if (!string.IsNullOrEmpty(endpoint))
        return new TableServiceClient(new Uri(endpoint), new DefaultAzureCredential());
    var connStr = config["Storage:ConnectionString"];
    if (!string.IsNullOrEmpty(connStr))
        return new TableServiceClient(connStr);
    throw new InvalidOperationException("No Table Storage endpoint or connection string configured.");
});

// TableClient for DeviceStatus
builder.Services.AddSingleton<TableClient>(sp => {
    var svc = sp.GetRequiredService<TableServiceClient>();
    var client = svc.GetTableClient("DeviceStatus");
    // Ensure the table exists at startup so projections don't fail later
    try {
        client.CreateIfNotExists();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogInformation("Ensured Azure Table exists: DeviceStatus");
    } catch (Exception ex) {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogWarning(ex, "Failed to ensure Azure Table exists: DeviceStatus");
    }
    return client;
});
builder.Services.AddScoped<IDeviceStatusStore, TableDeviceStatusStore>();

// TableClient for Devices (named registration)
builder.Services.AddSingleton(sp => {
    var svc = sp.GetRequiredService<TableServiceClient>();
    var client = svc.GetTableClient("Devices");
    try {
        client.CreateIfNotExists();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogInformation("Ensured Azure Table exists: Devices");
    } catch (Exception ex) {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogWarning(ex, "Failed to ensure Azure Table exists: Devices");
    }
    return client;
});
builder.Services.AddScoped<IDeviceStore>(sp => {
    var tableClient = sp.GetRequiredService<TableServiceClient>().GetTableClient("Devices");
    return new TableDeviceStore(tableClient);
});

// TableClient for DeviceActions
builder.Services.AddScoped<IDeviceActionStore>(sp => {
    var tableClient = sp.GetRequiredService<TableServiceClient>().GetTableClient("DeviceActions");
    return new TableDeviceActionStore(tableClient);
});

// TableClient for CommandQueue
builder.Services.AddScoped<ICommandQueueStore>(sp => {
    var tableClient = sp.GetRequiredService<TableServiceClient>().GetTableClient("CommandQueue");
    try {
        tableClient.CreateIfNotExists();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogInformation("Ensured Azure Table exists: CommandQueue");
    } catch (Exception ex) {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogWarning(ex, "Failed to ensure Azure Table exists: CommandQueue");
    }
    return new TableCommandQueueStore(tableClient);
});

// TableClient for CommandHistory
builder.Services.AddScoped<ICommandHistoryStore>(sp => {
    var tableClient = sp.GetRequiredService<TableServiceClient>().GetTableClient("CommandHistory");
    try {
        tableClient.CreateIfNotExists();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogInformation("Ensured Azure Table exists: CommandHistory");
    } catch (Exception ex) {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogWarning(ex, "Failed to ensure Azure Table exists: CommandHistory");
    }
    return new TableCommandHistoryStore(tableClient);
});

// Agent Auth Store - Table Storage for agent authentication (eliminates SQL dependency for auth)
builder.Services.AddScoped<IAgentAuthStore, TableAgentAuthStore>();

// TableClient for AtemState
builder.Services.AddScoped<IAtemStateStore>(sp => {
    var tableClient = sp.GetRequiredService<TableServiceClient>().GetTableClient("AtemState");
    try {
        tableClient.CreateIfNotExists();
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogInformation("Ensured Azure Table exists: AtemState");
    } catch (Exception ex) {
        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("TableSetup");
        logger.LogWarning(ex, "Failed to ensure Azure Table exists: AtemState");
    }
    return new TableAtemStateStore(tableClient);
});

// Activity Monitor for idle detection and SQL suspension
builder.Services.Configure<ActivityMonitorOptions>(builder.Configuration.GetSection("ActivityMonitor"));
builder.Services.AddSingleton<IActivityMonitor, DistributedActivityMonitor>();

// Client Notes Configuration
builder.Services.Configure<ClientNotesOptions>(builder.Configuration.GetSection("ClientNotes"));

// Twilio SMS service for notifications
builder.Services.Configure<TwilioConfig>(builder.Configuration.GetSection("Twilio"));
builder.Services.AddSingleton<ISmsService, TwilioSmsService>();

// Data Protection service for encrypting sensitive data (phone numbers)
builder.Services.AddDataProtection();
builder.Services.AddScoped<IDataProtectionService, AspNetCoreDataProtectionService>();

// Background service for device projection - DISABLED to stop continuous DB polling
// Devices and DeviceActions are now written directly to Table Storage when created/updated
// builder.Services.AddHostedService<DeviceProjectionHostedService>();

// Background service for table retention enforcement (deletes entries older than 8 days)
builder.Services.AddHostedService<TableRetentionEnforcementService>();

// Background service for device offline notifications (SMS)
builder.Services.AddHostedService<DeviceOfflineNotificationService>();

// Add Swagger/OpenAPI support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ProdControlAV API",
        Version = "v1",
        Description = "API for monitoring and controlling audio/visual production equipment. Includes multi-tenant authentication and device management endpoints.",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "ProdControlAV Security Team",
            Email = "security@prodcontrolav.com"
        }
    });
    // Add XML comments if available
    var xmlFile = $"ProdControlAV.API.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        options.IncludeXmlComments(xmlPath);
    // Add security definitions for cookie auth and JWT Bearer
    options.AddSecurityDefinition("cookieAuth", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Name = "prodcontrolav.auth",
        In = Microsoft.OpenApi.Models.ParameterLocation.Cookie,
        Description = "Cookie-based authentication for web users."
    });
    
    options.AddSecurityDefinition("bearerAuth", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Bearer authentication for agents."
    });
    
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "cookieAuth"
                }
            },
            new List<string>()
        },
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "bearerAuth"
                }
            },
            new List<string>()
        }
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    // options.SwaggerEndpoint("/swagger/v1/swagger.json", "ProdControlAV API v1");
    options.SwaggerEndpoint("https://localhost:5001/swagger/v1/swagger.json", "ProdControlAV API v1");
    
    options.DocumentTitle = "ProdControlAV API Documentation";
});

if (app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

app.UseCors("DevSpa");

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// Global tenant validation - must run BEFORE MapControllers()
// This middleware checks that authenticated users have a tenant claim
app.Use(async (ctx, next) =>
{
    // Skip tenant check for specific paths
    var path = ctx.Request.Path.Value?.ToLowerInvariant();
    if (path == "/signin" || path == "/signin/" || path?.StartsWith("/_framework") == true || 
        path?.StartsWith("/static") == true || path == "/" || path?.StartsWith("/api/auth/") == true ||
        path?.StartsWith("/api/agents/auth") == true || path?.StartsWith("/api/agents/heartbeat") == true)
    {
        await next();
        return;
    }

    // Check for both "tenant_id" (cookie auth) and "tenantId" (JWT auth)
    var tid = ctx.User.FindFirst("tenant_id")?.Value 
              ?? ctx.User.FindFirst("tenantId")?.Value;
    if (ctx.User.Identity?.IsAuthenticated == true && string.IsNullOrWhiteSpace(tid))
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "missing_tenant" });
        return;
    }
    await next();
});

app.MapControllers();
app.MapFallbackToFile("index.html");


app.Run();