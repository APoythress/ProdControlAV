using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProdControlAV.Core.Interfaces;

namespace ProdControlAV.Agent;

public class AgentPollingService : BackgroundService
{
    private readonly ILogger<AgentPollingService> _logger;
    private readonly ICommandQueue _commandQueue;
    private readonly IDeviceController _deviceController;
    private readonly INetworkMonitor _networkMonitor;
    private readonly string _deviceId = "192.168.1.10"; // Replace with real config/IP

    public AgentPollingService(
        ILogger<AgentPollingService> logger,
        ICommandQueue commandQueue,
        IDeviceController deviceController,
        INetworkMonitor networkMonitor)
    {
        _logger = logger;
        _commandQueue = commandQueue;
        _deviceController = deviceController;
        _networkMonitor = networkMonitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Polling for commands...");

            if (await _networkMonitor.IsDeviceOnlineAsync(_deviceId))
            {
                var commands = await _commandQueue.FetchPendingCommandsAsync(_deviceId);
                foreach (var cmd in commands)
                {
                    bool success = await _deviceController.SendCommandAsync(_deviceId, cmd);
                    // _logger.LogInformation($"Sent '{cmd}' to {_deviceId} – Success: {success}");
                }
            }
            else
            {
                _logger.LogWarning($"Device {_deviceId} is offline.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
