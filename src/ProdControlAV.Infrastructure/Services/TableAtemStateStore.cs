using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    public sealed class TableAtemStateStore : IAtemStateStore
    {
        private readonly TableClient _table;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public TableAtemStateStore(TableClient table) => _table = table;

        public async Task UpsertStateAsync(Guid tenantId, Guid deviceId, List<AtemInputDto> inputs, 
            Dictionary<string, long?> currentSources, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var rowKey = deviceId.ToString();
            
            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["InputsJson"] = JsonSerializer.Serialize(inputs, _jsonOptions),
                ["CurrentSourcesJson"] = JsonSerializer.Serialize(currentSources, _jsonOptions),
                ["LastUpdatedUtc"] = DateTimeOffset.UtcNow
            };
            
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        }

        public async Task<AtemStateDto?> GetStateAsync(Guid tenantId, Guid deviceId, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var rowKey = deviceId.ToString();
            
            try
            {
                var response = await _table.GetEntityAsync<TableEntity>(partitionKey, rowKey, cancellationToken: ct);
                var entity = response.Value;
                
                var inputsJson = entity.GetString("InputsJson");
                var currentSourcesJson = entity.GetString("CurrentSourcesJson");
                var lastUpdatedUtc = entity.GetDateTimeOffset("LastUpdatedUtc") ?? DateTimeOffset.UtcNow;
                
                if (string.IsNullOrEmpty(inputsJson) || string.IsNullOrEmpty(currentSourcesJson))
                    return null;
                
                var inputs = JsonSerializer.Deserialize<List<AtemInputDto>>(inputsJson, _jsonOptions) ?? new List<AtemInputDto>();
                var currentSources = JsonSerializer.Deserialize<Dictionary<string, long?>>(currentSourcesJson, _jsonOptions) ?? new Dictionary<string, long?>();
                
                return new AtemStateDto(deviceId, tenantId, inputs, currentSources, lastUpdatedUtc);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task DeleteAsync(Guid tenantId, Guid deviceId, CancellationToken ct)
        {
            try
            {
                await _table.DeleteEntityAsync(tenantId.ToString().ToLowerInvariant(), deviceId.ToString(), cancellationToken: ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Entity already deleted, ignore
            }
        }
    }
}
