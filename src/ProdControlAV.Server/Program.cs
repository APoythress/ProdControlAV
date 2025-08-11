using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProdControlAV.Infrastructure.Services;
using ProdControlAV.Core.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
    
    // Add mocking
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<ICommandQueue>(new JsonCommandQueue("Data/Commands"));
builder.Services.AddSingleton<IDeviceController>(new TelnetDeviceController());
builder.Services.AddSingleton<INetworkMonitor, PingNetworkMonitor>();
builder.Services.AddSingleton<IDeviceStatusRepository, InMemoryDeviceStatusRepository>();
builder.Services.AddHttpClient();

builder.Services.AddControllers();
builder.Services.AddAntiforgery(o => o.HeaderName = "X-CSRF-TOKEN"); // CSRF via header

var app = builder.Build();

// Middleware
app.UseCors("AllowAll");
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts(); // optional
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllers(); // expose your API + token endpoint

// Serve Blazor WASM app
app.MapFallbackToFile("index.html");

app.Run();
