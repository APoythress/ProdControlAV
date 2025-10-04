using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProdControlAV.Core.Models;
using ProdControlAV.Infrastructure.Services;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.API.Services
{
    public class DeviceProjectionHostedService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DeviceProjectionHostedService> _logger;
        private const int PollingIntervalSeconds = 10;
        private const int BatchSize = 50;
        private const int MaxRetries = 5;

        public DeviceProjectionHostedService(IServiceProvider serviceProvider, ILogger<DeviceProjectionHostedService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DeviceProjectionHostedService started");
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessOutboxBatchAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing outbox batch");
                }

                await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), stoppingToken);
            }
            
            _logger.LogInformation("DeviceProjectionHostedService stopped");
        }

        private async Task ProcessOutboxBatchAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deviceStore = scope.ServiceProvider.GetRequiredService<IDeviceStore>();

            // Get unprocessed entries ordered by creation time
            var entries = await db.OutboxEntries
                .Where(e => e.ProcessedUtc == null && e.RetryCount < MaxRetries)
                .OrderBy(e => e.CreatedUtc)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (entries.Count == 0)
                return;

            _logger.LogInformation("Processing {Count} outbox entries", entries.Count);

            foreach (var entry in entries)
            {
                try
                {
                    await ProcessEntryAsync(entry, deviceStore, ct);
                    entry.ProcessedUtc = DateTimeOffset.UtcNow;
                    entry.LastError = null;
                }
                catch (Exception ex)
                {
                    entry.RetryCount++;
                    entry.LastError = $"{ex.GetType().Name}: {ex.Message}";
                    _logger.LogError(ex, "Error processing outbox entry {Id} (attempt {Retry})", entry.Id, entry.RetryCount);
                }
            }

            await db.SaveChangesAsync(ct);
        }

        private async Task ProcessEntryAsync(OutboxEntry entry, IDeviceStore deviceStore, CancellationToken ct)
        {
            if (entry.EntityType == "Device")
            {
                if (entry.Operation == "Upsert" && !string.IsNullOrEmpty(entry.Payload))
                {
                    var device = JsonSerializer.Deserialize<Device>(entry.Payload);
                    if (device != null)
                    {
                        await deviceStore.UpsertAsync(
                            entry.TenantId,
                            device.Id,
                            device.Name,
                            device.Ip,
                            device.Type,
                            DateTimeOffset.UtcNow,
                            device.Model,
                            device.Brand,
                            device.Location,
                            device.AllowTelNet,
                            device.Port,
                            ct);
                        _logger.LogInformation("Projected device {DeviceId} for tenant {TenantId}", device.Id, entry.TenantId);
                    }
                }
                else if (entry.Operation == "Delete")
                {
                    await deviceStore.DeleteAsync(entry.TenantId, entry.EntityId, ct);
                    _logger.LogInformation("Deleted device {DeviceId} from tenant {TenantId}", entry.EntityId, entry.TenantId);
                }
            }
        }
    }
}
