using System;
using System.IO;
using System.Net.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProdControlAV.API.Services;
using ProdControlAV.Infrastructure.Services;
using ProdControlAV.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ProdControlAV.API.Auth;

var builder = WebApplication.CreateBuilder(args);

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

// Cookie auth
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, o =>
    {
        o.LoginPath = "/signin"; // optional
        o.Cookie.Name = "prodcontrolav.auth";
        o.SlidingExpiration = true;
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
});

// IMPORTANT: register the handler as Scoped (or Transient), not Singleton
builder.Services.AddScoped<IAuthorizationHandler, TenantMemberHandler>();

// Device/infra services
builder.Services.AddSingleton<ICommandQueue>(new JsonCommandQueue("Data/Commands"));
builder.Services.AddScoped<IDeviceCommandService, DeviceCommandService>();
builder.Services.AddSingleton<IDeviceController>(new TelnetDeviceController());
builder.Services.AddSingleton<IDeviceStatusRepository, InMemoryDeviceStatusRepository>();
builder.Services.AddSingleton<INetworkMonitor, PingNetworkMonitor>();
builder.Services.AddScoped<ITenantProvider, CompositeTenantProvider>();
builder.Services.AddScoped<IAgentAuth, AgentAuth>();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

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
    // Add security definition for cookie auth
    options.AddSecurityDefinition("cookieAuth", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Name = "prodcontrolav.auth",
        In = Microsoft.OpenApi.Models.ParameterLocation.Cookie,
        Description = "Cookie-based authentication."
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

app.MapControllers();
app.MapFallbackToFile("index.html");


// Global
app.Use(async (ctx, next) =>
{
    // reject if tenant is missing
    var tid = ctx.User.FindFirst("tenant_id")?.Value;
    if (ctx.User.Identity?.IsAuthenticated == true && string.IsNullOrWhiteSpace(tid))
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { error = "missing_tenant" });
        return;
    }
    await next();
});

// Allow anonymous access to /signin and static files
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLowerInvariant();
    if (path == "/signin" || path == "/signin/" || path.StartsWith("/_framework") || path.StartsWith("/static") || path == "/")
    {
        // Allow anonymous access
        await next();
        return;
    }
    await next();
});


app.Run();