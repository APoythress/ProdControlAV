using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProdControlAV.Agent;
using ProdControlAV.Core.Interfaces;
using ProdControlAV.Infrastructure.Services;

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<ICommandQueue>(provider =>
            new JsonCommandQueue("/home/pi/ProdControlAV/commands"));
        services.AddSingleton<IDeviceController>(provider =>
            new TelnetDeviceController());
        services.AddSingleton<INetworkMonitor, PingNetworkMonitor>();
        services.AddHostedService<AgentPollingService>();
    });

await builder.RunConsoleAsync();
