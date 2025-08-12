using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProdControlAV.Infrastructure.Services;
using ProdControlAV.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register services BEFORE Build()
builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddSingleton<ICommandQueue>(new JsonCommandQueue("Data/Commands"));
builder.Services.AddSingleton<IDeviceController>(new TelnetDeviceController());
builder.Services.AddSingleton<INetworkMonitor, PingNetworkMonitor>();
builder.Services.AddSingleton<IDeviceStatusRepository, InMemoryDeviceStatusRepository>();

builder.Services.AddHttpClient();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantProvider, HeaderTenantProvider>();

var dbSection = builder.Configuration.GetSection("Database");
var provider = dbSection["Provider"] ?? "Sqlite";

if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var configured = dbSection.GetSection("Sqlite")["ConnectionString"];

    string connectionString;
    if (string.IsNullOrWhiteSpace(configured))
    {
        // Fallback to ContentRoot/data/prodcontrol.db
        var dbPath = Path.Combine(builder.Environment.ContentRootPath, "data", "prodcontrol.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        connectionString = $"Data Source={dbPath}";
    }
    else if (configured.StartsWith("Data Source=./", StringComparison.OrdinalIgnoreCase))
    {
        // Resolve relative path against ContentRoot
        var relative = configured.Substring("Data Source=".Length).Trim(); // "./data/prodcontrol.db"
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

// Configure middleware AFTER Build()
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseCors("AllowAll");

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthorization();

app.MapControllers();         // expose your API
app.MapFallbackToFile("index.html"); // serve Blazor WASM

app.Run();