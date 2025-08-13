using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ProdControlAV.Infrastructure.Services;
using ProdControlAV.Core.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Controllers + JSON enum strings (optional)
builder.Services.AddControllers();

// Http + misc
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");

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

builder.Services.AddAuthorization();

// Device/infra services (as you had)
builder.Services.AddSingleton<ICommandQueue>(new JsonCommandQueue("Data/Commands"));
builder.Services.AddSingleton<IDeviceController>(new TelnetDeviceController());
builder.Services.AddSingleton<INetworkMonitor, PingNetworkMonitor>();
builder.Services.AddSingleton<IDeviceStatusRepository, InMemoryDeviceStatusRepository>();
builder.Services.AddScoped<ITenantProvider, HeaderTenantProvider>();

// Database (SQLite in dev)
var dbSection = builder.Configuration.GetSection("Database");
var provider = dbSection["Provider"] ?? "Sqlite";

if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var configured = dbSection.GetSection("Sqlite")["ConnectionString"];
    string connectionString;
    if (string.IsNullOrWhiteSpace(configured))
    {
        var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "prodcontrol.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        connectionString = $"Data Source={dbPath}";
    }
    else if (configured.StartsWith("Data Source=./", StringComparison.OrdinalIgnoreCase))
    {
        var relative = configured.Substring("Data Source=".Length).Trim();
        var dbPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        connectionString = $"Data Source={dbPath}";
    }
    else
    {
        connectionString = configured;
    }

    builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connectionString));
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseHttpsRedirection();

app.UseCors("DevSpa");

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();   // IMPORTANT
app.UseAuthorization();

app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
