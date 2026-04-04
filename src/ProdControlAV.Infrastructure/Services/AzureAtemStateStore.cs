// csharp
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ProdControlAV.Infrastructure.Services
{
    public class AzureAtemStateStore : IAtemStateStore
    {
        private readonly TableClient _table;
        private readonly ILogger<AzureAtemStateStore> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public AzureAtemStateStore(TableClient tableClient, ILogger<AzureAtemStateStore> logger)
        {
            _table = tableClient ?? throw new ArgumentNullException(nameof(tableClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            try
            {
                _table.CreateIfNotExists();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create or verify ATEM state table");
            }
        }

        public async Task UpsertStateAsync(Guid tenantId, Guid deviceId, List<AtemInputDto> inputs,
            Dictionary<string, long?> currentSources, CancellationToken ct)
        {
            var entity = new AtemStateEntity
            {
                PartitionKey = tenantId.ToString(),
                RowKey = deviceId.ToString(),
                LastUpdatedUtc = DateTimeOffset.UtcNow,
                InputsJson = JsonSerializer.Serialize(inputs ?? new List<AtemInputDto>(), _jsonOptions),
                CurrentSourcesJson = JsonSerializer.Serialize(currentSources ?? new Dictionary<string, long?>(), _jsonOptions)
            };

            try
            {
                await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upsert ATEM state for Tenant:{Tenant} Device:{Device}", tenantId, deviceId);
                throw;
            }
        }

        public async Task<AtemStateDto?> GetStateAsync(Guid tenantId, Guid deviceId, CancellationToken ct)
        {
            try
            {
                var response = await _table.GetEntityAsync<AtemStateEntity>(
                    partitionKey: tenantId.ToString(),
                    rowKey: deviceId.ToString(),
                    cancellationToken: ct).ConfigureAwait(false);

                var e = response.Value;

                var inputs = string.IsNullOrWhiteSpace(e.InputsJson)
                    ? new List<AtemInputDto>()
                    : JsonSerializer.Deserialize<List<AtemInputDto>>(e.InputsJson, _jsonOptions) ?? new List<AtemInputDto>();

                var currentSources = string.IsNullOrWhiteSpace(e.CurrentSourcesJson)
                    ? new Dictionary<string, long?>()
                    : JsonSerializer.Deserialize<Dictionary<string, long?>>(e.CurrentSourcesJson, _jsonOptions) ?? new Dictionary<string, long?>();

                return new AtemStateDto(
                    deviceId,
                    Guid.Parse(e.PartitionKey),
                    inputs,
                    currentSources,
                    e.LastUpdatedUtc);
            }
            catch (RequestFailedException rfe) when (rfe.Status == 404)
            {
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read ATEM state for Tenant:{Tenant} Device:{Device}", tenantId, deviceId);
                throw;
            }
        }

        public async Task DeleteAsync(Guid tenantId, Guid deviceId, CancellationToken ct)
        {
            try
            {
                await _table.DeleteEntityAsync(tenantId.ToString(), deviceId.ToString(), ETag.All, ct).ConfigureAwait(false);
            }
            catch (RequestFailedException rfe) when (rfe.Status == 404)
            {
                // already gone
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete ATEM state for Tenant:{Tenant} Device:{Device}", tenantId, deviceId);
                throw;
            }
        }
    }
}
