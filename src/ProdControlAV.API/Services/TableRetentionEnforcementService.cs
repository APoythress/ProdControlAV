using Azure.Data.Tables;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.API.Services
{
    /// <summary>
    /// Background service that deletes Table Storage entries older than 8 days for cost optimization.
    /// Runs daily and processes deletions in batches to handle large tenants efficiently.
    /// </summary>
    public class TableRetentionEnforcementService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<TableRetentionEnforcementService> _logger;
        private const int RetentionDays = 8;
        private const int ScanIntervalHours = 24; // Run once per day
        private const int BatchSize = 100;

        public TableRetentionEnforcementService(
            IServiceProvider serviceProvider, 
            ILogger<TableRetentionEnforcementService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("TableRetentionEnforcementService started - retention period: {Days} days", RetentionDays);

            // Wait a bit before first run to let the app fully start
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EnforceRetentionAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error enforcing retention policy");
                }

                await Task.Delay(TimeSpan.FromHours(ScanIntervalHours), stoppingToken);
            }

            _logger.LogInformation("TableRetentionEnforcementService stopped");
        }

        private async Task EnforceRetentionAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var tableServiceClient = scope.ServiceProvider.GetRequiredService<TableServiceClient>();
            
            var cutoffDate = DateTimeOffset.UtcNow.AddDays(-RetentionDays);
            _logger.LogInformation("Starting retention enforcement - deleting entries older than {CutoffDate}", cutoffDate);

            int totalDeleted = 0;
            var startTime = DateTimeOffset.UtcNow;

            try
            {
                // Get DeviceStatus table
                var deviceStatusTable = tableServiceClient.GetTableClient("DeviceStatus");
                var deletedFromStatus = await DeleteOldEntriesAsync(deviceStatusTable, cutoffDate, "LastSeenUtc", ct);
                totalDeleted += deletedFromStatus;

                _logger.LogInformation(
                    "Retention enforcement completed - deleted {Count} entries in {Duration}s",
                    totalDeleted,
                    (DateTimeOffset.UtcNow - startTime).TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to complete retention enforcement - deleted {Count} entries before error", totalDeleted);
                throw;
            }
        }

        private async Task<int> DeleteOldEntriesAsync(
            TableClient tableClient, 
            DateTimeOffset cutoffDate, 
            string timestampField,
            CancellationToken ct)
        {
            int deletedCount = 0;
            var tableName = tableClient.Name;

            try
            {
                _logger.LogInformation("Scanning table {TableName} for entries older than {CutoffDate}", tableName, cutoffDate);

                // Query for old entries - Azure Tables supports date comparison in filters
                var filter = $"{timestampField} lt datetime'{cutoffDate:yyyy-MM-ddTHH:mm:ssZ}'";
                var oldEntries = tableClient.QueryAsync<TableEntity>(filter, cancellationToken: ct);

                var batch = new List<TableEntity>();
                string? currentPartitionKey = null;

                await foreach (var entity in oldEntries)
                {
                    // Batch operations must have the same partition key
                    if (currentPartitionKey != null && currentPartitionKey != entity.PartitionKey)
                    {
                        // Process current batch
                        deletedCount += await DeleteBatchAsync(tableClient, batch, ct);
                        batch.Clear();
                    }

                    currentPartitionKey = entity.PartitionKey;
                    batch.Add(entity);

                    // Process batch when it reaches max size
                    if (batch.Count >= BatchSize)
                    {
                        deletedCount += await DeleteBatchAsync(tableClient, batch, ct);
                        batch.Clear();
                        currentPartitionKey = null;
                    }
                }

                // Process remaining batch
                if (batch.Count > 0)
                {
                    deletedCount += await DeleteBatchAsync(tableClient, batch, ct);
                }

                _logger.LogInformation("Deleted {Count} old entries from table {TableName}", deletedCount, tableName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting old entries from table {TableName} - deleted {Count} before error", 
                    tableName, deletedCount);
                throw;
            }

            return deletedCount;
        }

        private async Task<int> DeleteBatchAsync(TableClient tableClient, List<TableEntity> entities, CancellationToken ct)
        {
            if (entities.Count == 0)
                return 0;

            try
            {
                // All entities in the batch must have the same partition key for batch operations
                var partitionKey = entities[0].PartitionKey;
                
                // Use batch transaction for efficiency (up to 100 operations per batch)
                var batchActions = new List<Azure.Data.Tables.TableTransactionAction>();
                foreach (var entity in entities)
                {
                    batchActions.Add(new Azure.Data.Tables.TableTransactionAction(
                        Azure.Data.Tables.TableTransactionActionType.Delete, 
                        entity));
                }

                await tableClient.SubmitTransactionAsync(batchActions, ct);
                
                _logger.LogDebug("Deleted batch of {Count} entries from partition {PartitionKey}", 
                    entities.Count, partitionKey);
                
                return entities.Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete batch of {Count} entries - will retry individually", entities.Count);
                
                // Fall back to individual deletions if batch fails
                int deleted = 0;
                foreach (var entity in entities)
                {
                    try
                    {
                        await tableClient.DeleteEntityAsync(entity.PartitionKey, entity.RowKey, cancellationToken: ct);
                        deleted++;
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.LogWarning(deleteEx, "Failed to delete entity {PartitionKey}/{RowKey}", 
                            entity.PartitionKey, entity.RowKey);
                    }
                }
                
                return deleted;
            }
        }
    }
}
