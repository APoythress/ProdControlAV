using DotNetEnv;
using Microsoft.Extensions.Options;
using ProdControlAV.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

// Load environment variables from .env file at startup
Env.Load(); // Loads .env from current working directory

// Add environment variable configuration with specific prefix for security
builder.Configuration.AddEnvironmentVariables("PRODCONTROL_");

// Configure logging from configuration and add console provider
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();

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

// JWT auth service for token management
builder.Services.AddSingleton<IJwtAuthService, JwtAuthService>();

// HttpClient(s) - using the singleton JWT service
builder.Services.AddHttpClient<IStatusPublisher, StatusPublisher>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient<DeviceSource>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(5);
});
builder.Services.AddHttpClient<ICommandService, CommandService>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});
// Additional HTTP client for JWT auth service
builder.Services.AddHttpClient("JwtAuth", c => {
    c.Timeout = TimeSpan.FromMinutes(5);
});

// Register DeviceSource as both a singleton (for IDeviceSource) and a hosted service
builder.Services.AddSingleton<IDeviceSource>(sp => 
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var logger = sp.GetRequiredService<ILogger<DeviceSource>>();
    var apiOptions = sp.GetRequiredService<IOptions<ApiOptions>>();
    var jwtAuth = sp.GetRequiredService<IJwtAuthService>();
    var httpClient = httpClientFactory.CreateClient(nameof(DeviceSource));
    return new DeviceSource(httpClient, logger, apiOptions, jwtAuth);
});

// Hosted workers - DeviceSource must be registered as hosted service to start background processing
builder.Services.AddHostedService(sp => (DeviceSource)sp.GetRequiredService<IDeviceSource>());
builder.Services.AddHostedService<AgentService>();

await builder.Build().RunAsync();