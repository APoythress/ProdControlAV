using System;
using System.Net.Http;
using ProdControlAV.WebApp.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ProdControlAV.WebApp;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<DeviceStatusService>();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("https://your-api-host.com/") });

await builder.Build().RunAsync();
