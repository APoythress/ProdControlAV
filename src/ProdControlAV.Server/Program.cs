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
});
builder.Services.AddSingleton<ICommandQueue>(new JsonCommandQueue("Data/Commands"));
builder.Services.AddSingleton<IDeviceController>(new TelnetDeviceController());
builder.Services.AddSingleton<INetworkMonitor, PingNetworkMonitor>();

var app = builder.Build();

// Middleware
app.UseCors("AllowAll");
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapControllers();

// Serve Blazor WASM app
app.MapFallbackToFile("index.html");

app.Run();
