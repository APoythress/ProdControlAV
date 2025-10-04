# Azure Table Storage Device Projection Architecture

## Overview

The dashboard now uses Azure Table Storage for fast, scalable reads of devices and device actions, while SQL Server remains the source of truth for all CRUD operations. This architecture provides:

- **Fast dashboard queries** - O(1) partition queries instead of table scans
- **Eventual consistency** - Changes propagate within seconds with automatic retry
- **Resilience** - Outbox pattern ensures no data loss on transient failures
- **Scalability** - Table Storage handles high read throughput

## Architecture Components

### 1. SQL Server (Source of Truth)
- All device and device action CRUD operations write to SQL first
- Entity Framework models: `Device`, `DeviceAction`, `OutboxEntry`
- Multi-tenant with automatic query filtering by `TenantId`

### 2. Outbox Table
- Tracks pending projections to Azure Table Storage
- Fields: `Id`, `TenantId`, `EntityType`, `EntityId`, `Operation`, `Payload`, `CreatedUtc`, `ProcessedUtc`, `RetryCount`, `LastError`
- Operations: `Upsert` (create/update) or `Delete`
- Indexed on `(ProcessedUtc, CreatedUtc)` for efficient polling

### 3. DeviceProjectionHostedService
- Background service that runs continuously
- Polls every 10 seconds for unprocessed outbox entries
- Processes up to 50 entries per batch
- Retries failed projections up to 5 times with exponential backoff
- Logs all operations for observability

### 4. Azure Table Storage

#### Devices Table
- **Table Name**: `Devices`
- **PartitionKey**: `tenantId` (lowercase GUID)
- **RowKey**: `deviceId` (GUID)
- **Properties**:
  - `Name` (string)
  - `IpAddress` (string)
  - `Type` (string)
  - `Model` (string)
  - `Brand` (string)
  - `Location` (string, nullable)
  - `AllowTelNet` (boolean)
  - `Port` (int)
  - `CreatedUtc` (DateTimeOffset)

#### DeviceActions Table
- **Table Name**: `DeviceActions`
- **PartitionKey**: `tenantId` (lowercase GUID)
- **RowKey**: `actionId` (GUID)
- **Properties**:
  - `DeviceId` (string GUID)
  - `ActionName` (string)

### 5. API Endpoints

#### Read (from Table Storage)
- `GET /api/devices/devices` - Returns all devices for tenant (from Tables)
- `GET /api/devices/actions` - Returns all actions for tenant (from Tables)
- Both use efficient partition queries: `WHERE PartitionKey = '{tenantId}'`

#### Write (to SQL + Outbox)
- `POST /api/devices` - Create device → SQL + Outbox
- `PUT /api/devices/{id}` - Update device → SQL + Outbox
- `DELETE /api/devices/{id}` - Delete device → SQL + Outbox
- `POST /api/commands` - Create device action → SQL + Outbox

## Data Flow

### Device Creation
1. User submits POST to `/api/devices`
2. API validates request and creates `Device` entity
3. API creates `OutboxEntry` with `Operation=Upsert` and serialized device
4. Both saved to SQL in single transaction
5. Background service picks up outbox entry within 10 seconds
6. Service calls `IDeviceStore.UpsertAsync()` to write to Table Storage
7. Outbox entry marked as `ProcessedUtc = DateTimeOffset.UtcNow`
8. Dashboard GET request immediately sees new device

### Device Update
Same flow as creation - outbox entry created with updated device data.

### Device Deletion
1. User submits DELETE to `/api/devices/{id}`
2. API removes `Device` from SQL
3. API creates `OutboxEntry` with `Operation=Delete` (no payload needed)
4. Background service deletes from Table Storage
5. Dashboard GET request no longer sees device

### Dashboard Loading
1. Blazor app calls `GET /api/devices/devices`
2. API queries Table Storage with partition key = current tenantId
3. Returns `DashboardDeviceDto` list (typically <100ms for hundreds of devices)
4. Dashboard polls status separately every 10 seconds

## Configuration

### appsettings.json
```json
{
  "Storage": {
    "TablesEndpoint": "https://{account}.table.core.windows.net",
    "ConnectionString": "DefaultEndpointsProtocol=https;AccountName={account};AccountKey={key};EndpointSuffix=core.windows.net"
  }
}
```

Use either `TablesEndpoint` with Managed Identity (production) or `ConnectionString` (development).

### Service Registration (Program.cs)
```csharp
// Table Service Client
builder.Services.AddSingleton<TableServiceClient>(sp => {
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = config["Storage:TablesEndpoint"];
    if (!string.IsNullOrEmpty(endpoint))
        return new TableServiceClient(new Uri(endpoint), new DefaultAzureCredential());
    var connStr = config["Storage:ConnectionString"];
    if (!string.IsNullOrEmpty(connStr))
        return new TableServiceClient(connStr);
    throw new InvalidOperationException("No Table Storage endpoint configured.");
});

// Device and Action stores
builder.Services.AddScoped<IDeviceStore>(sp => {
    var tableClient = sp.GetRequiredService<TableServiceClient>().GetTableClient("Devices");
    return new TableDeviceStore(tableClient);
});

builder.Services.AddScoped<IDeviceActionStore>(sp => {
    var tableClient = sp.GetRequiredService<TableServiceClient>().GetTableClient("DeviceActions");
    return new TableDeviceActionStore(tableClient);
});

// Background projection service
builder.Services.AddHostedService<DeviceProjectionHostedService>();
```

## Deployment Steps

### 1. Database Migration
```bash
cd src/ProdControlAV.API
dotnet ef database update
```

This applies migration `20251004175246_AddOutboxEntry` which creates the `OutboxEntries` table.

### 2. Create Azure Table Storage Tables
Using Azure Portal, CLI, or Storage Explorer:
```bash
az storage table create --name Devices --account-name {your-account}
az storage table create --name DeviceActions --account-name {your-account}
az storage table create --name DeviceStatus --account-name {your-account}
```

### 3. Initial Projection (One-Time)
After migration, existing devices need to be projected to Table Storage. Run this SQL to populate the outbox:

```sql
-- Project all existing devices
INSERT INTO OutboxEntries (Id, TenantId, EntityType, EntityId, Operation, Payload, CreatedUtc, ProcessedUtc, RetryCount, LastError)
SELECT 
    NEWID() AS Id,
    TenantId,
    'Device' AS EntityType,
    Id AS EntityId,
    'Upsert' AS Operation,
    (SELECT * FROM Devices d2 WHERE d2.Id = d.Id FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS Payload,
    GETUTCDATE() AS CreatedUtc,
    NULL AS ProcessedUtc,
    0 AS RetryCount,
    NULL AS LastError
FROM Devices d;

-- Project all existing device actions
INSERT INTO OutboxEntries (Id, TenantId, EntityType, EntityId, Operation, Payload, CreatedUtc, ProcessedUtc, RetryCount, LastError)
SELECT 
    NEWID() AS Id,
    TenantId,
    'DeviceAction' AS EntityType,
    ActionId AS EntityId,
    'Upsert' AS Operation,
    (SELECT * FROM DeviceActions da2 WHERE da2.ActionId = da.ActionId FOR JSON PATH, WITHOUT_ARRAY_WRAPPER) AS Payload,
    GETUTCDATE() AS CreatedUtc,
    NULL AS ProcessedUtc,
    0 AS RetryCount,
    NULL AS LastError
FROM DeviceActions da;
```

The background service will process these entries within a few minutes.

### 4. Deploy Application
Deploy the updated API and WebApp. The background service starts automatically.

### 5. Monitor Projection
Check logs for `DeviceProjectionHostedService` entries:
```
[Information] Processing 10 outbox entries
[Information] Projected device {DeviceId} for tenant {TenantId}
```

Check for failed entries:
```sql
SELECT * FROM OutboxEntries 
WHERE ProcessedUtc IS NULL 
  AND RetryCount >= 5
ORDER BY CreatedUtc DESC;
```

## Monitoring and Operations

### Health Checks
Monitor outbox processing lag:
```sql
SELECT 
    COUNT(*) AS PendingEntries,
    MIN(CreatedUtc) AS OldestEntry,
    MAX(RetryCount) AS MaxRetries
FROM OutboxEntries
WHERE ProcessedUtc IS NULL;
```

Should typically be < 10 entries and < 30 seconds old.

### Performance Metrics
- **Dashboard load time**: <100ms for list of 100 devices
- **Projection lag**: 10-30 seconds (10s poll interval + processing time)
- **Retry behavior**: Exponential backoff with 5 max retries

### Common Issues

#### Outbox entries not processing
- Check `DeviceProjectionHostedService` is running in logs
- Check Azure Table Storage connectivity
- Review `LastError` field in OutboxEntries table

#### Stale data in dashboard
- Check outbox lag (query above)
- Verify background service logs for errors
- Check Azure Table Storage account status

#### High retry count
- Indicates transient Azure errors or network issues
- Entries with RetryCount >= 5 are abandoned (needs manual intervention)
- Review `LastError` for root cause

## Security

### Multi-Tenancy
- All queries filtered by `TenantId` (partition key)
- Cross-tenant access impossible at Table Storage level
- SQL query filters enforced by EF Core

### Authentication
- Table Storage uses Azure AD Managed Identity (production) or connection string (dev)
- API endpoints require `IsMember` policy (validated tenant membership)

### Data Isolation
- Each tenant's data in separate partition
- No shared row keys between tenants
- Automatic cleanup on tenant deletion (via outbox)

## Cost Optimization

### Storage Costs
- Tables: ~$0.07/GB/month
- Transactions: $0.00036/10,000 operations
- Typical monthly cost: <$10 for 10K devices with 1M operations

### Optimization Tips
1. **Keep projections minimal** - Only include fields needed by dashboard
2. **Batch deletions** - Delete multiple devices in one API call
3. **Monitor failed entries** - Fix systemic issues to avoid retry overhead
4. **Use partition queries** - Always filter by tenantId

## Future Enhancements

### Potential Improvements
1. **Real-time sync** - Replace polling with Azure Service Bus
2. **Change data capture** - Use SQL Server CDC instead of outbox
3. **Caching layer** - Add Redis for even faster reads
4. **Analytics** - Stream changes to Azure Data Lake for reporting
5. **Conflict resolution** - Handle concurrent updates better

### Breaking Changes to Avoid
- Don't remove SQL tables - always the source of truth
- Don't change partition key scheme - requires full rewrite
- Don't bypass outbox - ensures consistency
