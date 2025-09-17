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
    // If BaseUrl is not set in config, try environment variable
    var envApiUrl = Environment.GetEnvironmentVariable("PRODCONTROL_API_URL");
    if (!string.IsNullOrWhiteSpace(envApiUrl))
    {
        options.BaseUrl = envApiUrl;
    }
    
    // If ApiKey is not set in config, try environment variable
    if (string.IsNullOrWhiteSpace(options.ApiKey))
    {
        options.ApiKey = Environment.GetEnvironmentVariable("PRODCONTROL_AGENT_APIKEY");
    }
    
    // Validate that BaseUrl is provided and is a valid URI
    if (string.IsNullOrWhiteSpace(options.BaseUrl))
    {
        throw new InvalidOperationException(
            "Agent API Base URL must be provided either in configuration (Api:BaseUrl) " +
            "or via environment variable (PRODCONTROL_API_URL)");
    }
    
    if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
    {
        throw new InvalidOperationException(
            $"Agent API Base URL '{options.BaseUrl}' is not a valid URI");
    }
    
    if (baseUri.Scheme != "https" && baseUri.Scheme != "http")
    {
        throw new InvalidOperationException(
            $"Agent API Base URL '{options.BaseUrl}' must use http or https scheme");
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