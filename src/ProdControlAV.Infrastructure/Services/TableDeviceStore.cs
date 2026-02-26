using Azure;
using Azure.Data.Tables;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ProdControlAV.Infrastructure.Services
{
    public sealed class TableDeviceStore : IDeviceStore
    {
        private readonly TableClient _table;
        public TableDeviceStore(TableClient table) => _table = table;

        public async Task UpsertAsync(Guid tenantId, Guid deviceId, string name, string ipAddress, string type, 
            DateTimeOffset createdUtc, string? model, string? brand, string? location, bool allowTelNet, int port,
            bool smsAlertsEnabled, CancellationToken ct)
        {
            var entity = new TableEntity(tenantId.ToString().ToLowerInvariant(), deviceId.ToString())
            {
                ["Name"] = name,
                ["IpAddress"] = ipAddress,
                ["Ip"] = ipAddress, // write both keys to be backward/forward compatible
                ["Type"] = type,
                ["CreatedUtc"] = createdUtc,
                ["Model"] = model ?? "",
                ["Brand"] = brand ?? "",
                ["Location"] = location ?? "",
                ["AllowTelNet"] = allowTelNet,
                ["Port"] = port,
                ["SmsAlertsEnabled"] = smsAlertsEnabled
            };
            // Use Merge so we don't wipe out any manual/custom columns that might be present
            await _table.UpsertEntityAsync(entity, TableUpdateMode.Merge, ct);
        }

        public async Task UpdateSmsLastSentAsync(Guid tenantId, Guid deviceId, DateTimeOffset? lastSentUtc, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var rowKey = deviceId.ToString();

            // Use DateTimeOffset.MinValue as the sentinel "cleared/null" value because
            // Azure Table Storage merge-updates cannot delete individual properties.
            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["LastSentSMSUtc"] = lastSentUtc.HasValue ? (object)lastSentUtc.Value : DateTimeOffset.MinValue
            };

            try
            {
                await _table.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Device not yet projected – skip silently
            }
        }

        public async Task UpsertStatusAsync(Guid tenantId, Guid deviceId, string status, DateTimeOffset lastSeenUtc, DateTimeOffset lastPolledUtc, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var rowKey = deviceId.ToString();
            
            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["Status"] = status,
                ["LastSeenUtc"] = lastSeenUtc,
                ["LastPolledUtc"] = lastPolledUtc
            };
            
            try
            {
                // Use UpdateEntity with Merge mode instead of UpsertEntity
                // This will fail with 404 if the entity doesn't exist, preventing partial entity creation
                // The device must be fully projected by DeviceProjectionHostedService before status updates work
                await _table.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Device doesn't exist in table yet - skip status update silently
                // The device will be projected by DeviceProjectionHostedService and future status updates will work
            }
        }

        public async Task UpsertRecordingStatusAsync(Guid tenantId, Guid deviceId, bool recordingStatus, CancellationToken ct)
        {
            var partitionKey = tenantId.ToString().ToLowerInvariant();
            var rowKey = deviceId.ToString();
            
            var entity = new TableEntity(partitionKey, rowKey)
            {
                ["RecordingStatus"] = recordingStatus
            };
            
            try
            {
                // Use UpdateEntity with Merge mode - only update RecordingStatus field
                await _table.UpdateEntityAsync(entity, ETag.All, TableUpdateMode.Merge, ct);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Device doesn't exist in table yet - skip recording status update silently
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

        public async IAsyncEnumerable<DeviceDto> GetAllForTenantAsync(Guid tenantId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            var query = _table.QueryAsync<TableEntity>(x => x.PartitionKey == tenantId.ToString().ToLowerInvariant(), cancellationToken: ct);
            await foreach (var e in query)
            {
                // Safely convert values from the TableEntity to avoid InvalidCastException when the stored
                // value type differs from the expected type (for example boolean stored where a string
                // is expected). We prefer tolerant conversions (ToString, Convert.ToInt32, DateTime -> DateTimeOffset)

                object? v;

                // Name (non-nullable string)
                string name = (e.TryGetValue("Name", out v) && v != null) ? (v is string sv ? sv : Convert.ToString(v) ?? string.Empty) : string.Empty;

                // IpAddress (non-nullable string) - accept either IpAddress or Ip for compatibility
                string ipAddress = string.Empty;
                if ((e.TryGetValue("IpAddress", out v) && v != null))
                {
                    ipAddress = (v is string siv ? siv : Convert.ToString(v) ?? string.Empty);
                }
                else if ((e.TryGetValue("Ip", out v) && v != null))
                {
                    ipAddress = (v is string siv2 ? siv2 : Convert.ToString(v) ?? string.Empty);
                }

                // Type (non-nullable string)
                string type = (e.TryGetValue("Type", out v) && v != null) ? (v is string st ? st : Convert.ToString(v) ?? string.Empty) : string.Empty;

                // CreatedUtc (DateTimeOffset)
                DateTimeOffset createdUtc;
                if (e.TryGetValue("CreatedUtc", out v) && v != null)
                {
                    if (v is DateTimeOffset dto) createdUtc = dto;
                    else if (v is DateTime dt) createdUtc = new DateTimeOffset(dt);
                    else if (v is string s && DateTimeOffset.TryParse(s, out var parsed)) createdUtc = parsed;
                    else createdUtc = DateTimeOffset.MinValue;
                }
                else
                {
                    createdUtc = DateTimeOffset.MinValue;
                }

                // Optional strings
                string? model = (e.ContainsKey("Model") && e["Model"] != null) ? Convert.ToString(e["Model"]) : null;
                string? brand = (e.ContainsKey("Brand") && e["Brand"] != null) ? Convert.ToString(e["Brand"]) : null;
                string? location = (e.ContainsKey("Location") && e["Location"] != null) ? Convert.ToString(e["Location"]) : null;

                // AllowTelNet (bool)
                bool allowTelNet = false;
                if (e.TryGetValue("AllowTelNet", out v) && v != null)
                {
                    if (v is bool b) allowTelNet = b;
                    else
                    {
                        // tolerate string or numeric representations
                        var s = Convert.ToString(v);
                        if (bool.TryParse(s, out var pb)) allowTelNet = pb;
                        else if (int.TryParse(s, out var pi)) allowTelNet = pi != 0;
                    }
                }

                // Port (int)
                int port = 80;
                if (e.TryGetValue("Port", out v) && v != null)
                {
                    try { port = Convert.ToInt32(v); } catch { port = 80; }
                }

                // Status (optional string)
                string? status = (e.ContainsKey("Status") && e["Status"] != null) ? Convert.ToString(e["Status"]) : null;

                // LastSeenUtc / LastPolledUtc (nullable DateTimeOffset)
                DateTimeOffset? lastSeen = null;
                if (e.TryGetValue("LastSeenUtc", out v) && v != null)
                {
                    if (v is DateTimeOffset dto2) lastSeen = dto2;
                    else if (v is DateTime dt2) lastSeen = new DateTimeOffset(dt2);
                    else if (v is string s2 && DateTimeOffset.TryParse(s2, out var p2)) lastSeen = p2;
                }

                DateTimeOffset? lastPolled = null;
                if (e.TryGetValue("LastPolledUtc", out v) && v != null)
                {
                    if (v is DateTimeOffset dto3) lastPolled = dto3;
                    else if (v is DateTime dt3) lastPolled = new DateTimeOffset(dt3);
                    else if (v is string s3 && DateTimeOffset.TryParse(s3, out var p3)) lastPolled = p3;
                }

                // HealthMetric (nullable double)
                double? healthMetric = null;
                if (e.TryGetValue("HealthMetric", out v) && v != null)
                {
                    try { healthMetric = Convert.ToDouble(v); } catch { healthMetric = null; }
                }

                // RecordingStatus (nullable bool) - for Video devices
                bool? recordingStatus = null;
                if (e.TryGetValue("RecordingStatus", out v) && v != null)
                {
                    if (v is bool b) recordingStatus = b;
                    else
                    {
                        var s = Convert.ToString(v);
                        if (bool.TryParse(s, out var pb)) recordingStatus = pb;
                        else if (int.TryParse(s, out var pi)) recordingStatus = pi != 0;
                    }
                }

                // SmsAlertsEnabled (bool, default true)
                bool smsAlertsEnabled = true;
                if (e.TryGetValue("SmsAlertsEnabled", out v) && v != null)
                {
                    if (v is bool bsms) smsAlertsEnabled = bsms;
                    else
                    {
                        var s = Convert.ToString(v);
                        if (bool.TryParse(s, out var pb)) smsAlertsEnabled = pb;
                        else if (int.TryParse(s, out var pi)) smsAlertsEnabled = pi != 0;
                    }
                }

                // LastSentSMSUtc (nullable DateTimeOffset; DateTimeOffset.MinValue treated as null)
                DateTimeOffset? lastSentSmsUtc = null;
                if (e.TryGetValue("LastSentSMSUtc", out v) && v != null)
                {
                    DateTimeOffset parsed = DateTimeOffset.MinValue;
                    if (v is DateTimeOffset dto4) parsed = dto4;
                    else if (v is DateTime dt4) parsed = new DateTimeOffset(dt4);
                    else if (v is string s4 && DateTimeOffset.TryParse(s4, out var p4)) parsed = p4;
                    if (parsed != DateTimeOffset.MinValue)
                        lastSentSmsUtc = parsed;
                }

                yield return new DeviceDto(
                    Guid.Parse(e.RowKey),
                    name,
                    ipAddress,
                    type,
                    tenantId,
                    createdUtc,
                    string.IsNullOrWhiteSpace(model) ? null : model,
                    string.IsNullOrWhiteSpace(brand) ? null : brand,
                    string.IsNullOrWhiteSpace(location) ? null : location,
                    allowTelNet,
                    port,
                    string.IsNullOrWhiteSpace(status) ? null : status,
                    lastSeen,
                    lastPolled,
                    healthMetric,
                    recordingStatus,
                    smsAlertsEnabled,
                    lastSentSmsUtc
                );
            }
        }
    }
}
