using ProdControlAV.Agent.Models;
using ProdControlAV.Agent.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
});

// Bind options
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Polling"));
builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Api"));

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
