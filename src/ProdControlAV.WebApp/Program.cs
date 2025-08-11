using System;
using System.Net.Http;
using ProdControlAV.WebApp.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ProdControlAV.WebApp;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<DeviceStatusService>();
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("http://192.168.1.50/") });
builder.Services.AddHttpClient<DeviceApiClient>(client =>
{
    client.BaseAddress = new Uri("http://192.168.1.50/"); // Pico API IP
});

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN"; // Token will be sent in a header
});

await builder.Build().RunAsync();
