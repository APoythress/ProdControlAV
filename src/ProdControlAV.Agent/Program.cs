using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProdControlAV.Agent.Models;
using ProdControlAV.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

// Add environment variable configuration with specific prefix for security
builder.Configuration.AddEnvironmentVariables("PRODCONTROL_");

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
});

// Bind options
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Polling"));
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Api"));

// Post-configure ApiOptions to handle environment variable fallback and validation
builder.Services.PostConfigure<ApiOptions>(options =>
{
    // If ApiKey is not set in config, try environment variable
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
    }
    
    // If TenantId is not set in config, try environment variable
    if (options.TenantId == null || options.TenantId == Guid.Empty)
    {
        var tenantIdStr = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_TENANTID");
        if (!string.IsNullOrWhiteSpace(tenantIdStr) && Guid.TryParse(tenantIdStr, out var tenantId))
        {
            options.TenantId = tenantId;
        }
    }
    
    // Validate that ApiKey is provided
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        throw new InvalidOperationException(
            "Agent API Key must be provided either in configuration (Api:ApiKey) " +
            "or via environment variable (PRODCONTROL_AGENT_APIKEY)");
    }
    
    // Validate API key format (should be at least 32 characters for security)
    if (options.ApiKey.Length < 32)
    {
        throw new InvalidOperationException(
            "Agent API Key must be at least 32 characters long for security");
    }
    
    // Validate that TenantId is provided
    if (options.TenantId == null || options.TenantId == Guid.Empty)
    {
        throw new InvalidOperationException(
            "Agent Tenant ID must be provided either in configuration (Api:TenantId) " +
            "or via environment variable (PRODCONTROL_AGENT_TENANTID)");
    }
});

// HttpClient(s)
builder.Services.AddHttpClient<IStatusPublisher, StatusPublisher>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient<IDeviceSource, DeviceSource>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient<ICommandService, CommandService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});

// Hosted worker
builder.Services.AddHostedService<AgentService>();

await builder.Build().RunAsync();