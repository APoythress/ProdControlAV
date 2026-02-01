# Cost Optimization - Architecture Planning Guide

## Overview

This document guides coding agents through cost optimization strategies in ProdControlAV. Cost efficiency is a **core architectural principle** and must be considered in every technical decision.

## Core Cost Principles

### 1. Storage Hierarchy
- **Azure Table Storage**: $0.045/GB/month (~$0.50/month for typical usage)
- **Azure Queue Storage**: $0.0004/10,000 operations
- **Azure SQL Database**: $5-200/month depending on tier
- **Strategy**: Use cheapest appropriate storage for each use case

### 2. Compute Efficiency
- **Idle detection**: Suspend operations when no users active
- **Queue-based polling**: Eliminate constant SQL queries
- **Batch operations**: Reduce transaction counts
- **Background processing**: Off-peak expensive operations

### 3. Cost Monitoring
- **Track storage operations**: Log Table/Queue transaction counts
- **Monitor SQL DTU usage**: Alert on unexpected spikes
- **Measure per-tenant costs**: Enable chargeback/showback
- **Optimize incrementally**: Identify and fix cost hot spots

## Current Cost Architecture

### Before Optimization (SQL-Heavy)

```
Monthly Costs (10 agents, 5 users, 20 devices):
├── Azure SQL Database (S1)          $30.00
├── Table Storage                     $0.10
├── Queue Storage                     $0.05
├── Blob Storage                      $2.00
└── TOTAL                            $32.15/month
```

### After Optimization (Table Storage First)

```
Monthly Costs (10 agents, 5 users, 20 devices):
├── Azure SQL Database (Basic, idle)  $5.00
├── Table Storage                     $0.50
├── Queue Storage                     $0.10
├── Blob Storage                      $2.00
└── TOTAL                             $7.60/month

SAVINGS: $24.55/month (76% reduction)
```

## Cost-Effective Architecture Decisions

### Decision Matrix: Storage Selection

| Use Case | SQL | Table Storage | Queue | Rationale |
|----------|-----|---------------|-------|-----------|
| Device metadata | ✓ | ✓ | ✗ | SQL for audit, Table for reads |
| Device status | ✗ | ✓ | ✗ | High frequency, Table only |
| Agent commands | ✓ | ✗ | ✓ | SQL for audit, Queue for delivery |
| Command history | ✓ | ✗ | ✗ | Compliance requires SQL |
| User accounts | ✓ | ✗ | ✗ | Low frequency, SQL only |
| Activity monitoring | ✗ | ✓ | ✗ | High frequency, Table only |
| Configuration | ✓ | ✗ | ✗ | Low frequency, SQL only |

### Cost Per Operation

| Operation | SQL (Basic) | Table Storage | Queue Storage |
|-----------|-------------|---------------|---------------|
| Single read | ~0.0001 DTU | $0.00004 | N/A |
| Single write | ~0.001 DTU | $0.0005 | $0.00004 |
| Query (100 rows) | ~0.01 DTU | $0.004 | N/A |
| Batch write (10) | ~0.005 DTU | $0.005 | $0.0004 |
| **Cost at 1M ops/month** | **$5-30** | **$0.50** | **$0.40** |

**Key Insight**: Table/Queue Storage is 10-100x cheaper for high-frequency operations

## Implementing Cost Optimizations

### 1. Eliminate SQL Queries in Hot Paths

**Before (Expensive):**
```csharp
// Agent polls SQL every 10 seconds
public async Task<List<Device>> GetDevicesForAgentAsync(Guid agentId, CancellationToken ct)
{
    return await _db.Devices
        .Where(d => d.TenantId == _agentTenantId)
        .ToListAsync(ct);
}

// Cost: ~8,640 SQL queries/day/agent = 259,200 queries/month (10 agents)
// DTU impact: Requires S1 tier ($30/month minimum)
```

**After (Cheap):**
```csharp
// Agent reads from Table Storage
public async IAsyncEnumerable<DeviceDto> GetDevicesForAgentAsync(Guid tenantId, CancellationToken ct)
{
    var table = _tableServiceClient.GetTableClient("Devices");
    var filter = $"PartitionKey eq '{tenantId}'";
    
    await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter, cancellationToken: ct))
    {
        yield return MapToDto(entity);
    }
}

// Cost: ~$0.10/month for 259,200 operations
// Savings: $29.90/month (99.7% reduction)
```

### 2. Use Queue Storage for Commands

**Before (Expensive):**
```csharp
// Agent polls SQL for commands every 10 seconds
public async Task<AgentCommand?> GetNextCommandAsync(Guid agentId, CancellationToken ct)
{
    var command = await _db.AgentCommands
        .Where(c => c.AgentId == agentId && c.TakenUtc == null)
        .OrderBy(c => c.CreatedUtc)
        .FirstOrDefaultAsync(ct);
    
    if (command != null)
    {
        command.TakenUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
    
    return command;
}

// Cost: 2 SQL operations (read + write) every 10 seconds
// DTU impact: Constant database load, prevents idling
```

**After (Cheap):**
```csharp
// Agent polls Queue Storage
public async Task<AgentCommand?> ReceiveCommandAsync(Guid tenantId, Guid agentId, CancellationToken ct)
{
    var queueName = $"pcav-{tenantId}-{agentId}".ToLowerInvariant();
    var queue = _queueServiceClient.GetQueueClient(queueName);
    
    var response = await queue.ReceiveMessageAsync(TimeSpan.FromSeconds(60), ct);
    
    if (response.Value == null)
        return null;
    
    var message = response.Value;
    var command = JsonSerializer.Deserialize<AgentCommand>(
        Encoding.UTF8.GetString(Convert.FromBase64String(message.MessageText)));
    
    return command;
}

// Cost: ~$0.35/month for 259,200 operations
// Savings: SQL can idle completely during command polling
```

### 3. Implement Idle Detection

**Idle Detection Service:**
```csharp
public class IdleDetectionOptions
{
    public int IdleTimeoutMinutes { get; set; } = 10;
    public bool EnableIdleSuspension { get; set; } = true;
}

public class DistributedActivityMonitor : IActivityMonitor
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<DistributedActivityMonitor> _logger;
    private readonly IdleDetectionOptions _options;
    
    public async Task<bool> IsSystemIdleAsync(CancellationToken ct)
    {
        if (!_options.EnableIdleSuspension)
            return false;
        
        var table = _tableServiceClient.GetTableClient("SystemActivity");
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-_options.IdleTimeoutMinutes);
        
        // Check for recent user or agent activity
        var filter = $"Timestamp gt datetime'{cutoff:yyyy-MM-ddTHH:mm:ssZ}'";
        var recentActivity = table.QueryAsync<TableEntity>(filter: filter, cancellationToken: ct);
        
        var hasActivity = await recentActivity.AnyAsync(ct);
        
        if (!hasActivity)
        {
            _logger.LogInformation("System is idle - no activity in last {Minutes} minutes", 
                _options.IdleTimeoutMinutes);
        }
        
        return !hasActivity;
    }
    
    public async Task RecordUserActivityAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var table = _tableServiceClient.GetTableClient("SystemActivity");
        await table.CreateIfNotExistsAsync(ct);
        
        var entity = new TableEntity("Activity", $"User-{tenantId}-{userId}")
        {
            { "Type", "User" },
            { "TenantId", tenantId.ToString() },
            { "UserId", userId.ToString() },
            { "LastActivityUtc", DateTimeOffset.UtcNow }
        };
        
        await table.UpsertEntityAsync(entity, cancellationToken: ct);
    }
}
```

**Using Idle Detection:**
```csharp
public class DeviceProjectionHostedService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check if system is idle
                if (await _activityMonitor.IsSystemIdleAsync(stoppingToken))
                {
                    _logger.LogDebug("System idle, skipping SQL operations");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }
                
                // Perform SQL operations (only when active)
                await ProcessOutboxEntriesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in background service");
            }
            
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}

// Cost Impact:
// - Before: SQL operations every 10 seconds (24/7)
// - After: SQL operations only when users active
// - Savings: ~80% reduction in SQL operations during off-hours
```

### 4. Batch Operations

**Before (Many Small Operations):**
```csharp
// Upload each device status individually
foreach (var device in devices)
{
    var status = await MonitorDeviceAsync(device, ct);
    await _statusPublisher.PublishStatusAsync(status, ct);
}

// Cost: 20 HTTP requests, 20 Table Storage writes
```

**After (Batched):**
```csharp
// Collect statuses and upload in batches
var statusBatch = new List<DeviceStatus>();

foreach (var device in devices)
{
    var status = await MonitorDeviceAsync(device, ct);
    statusBatch.Add(status);
    
    if (statusBatch.Count >= 10)
    {
        await _statusPublisher.PublishBatchAsync(statusBatch, ct);
        statusBatch.Clear();
    }
}

// Upload remaining
if (statusBatch.Any())
{
    await _statusPublisher.PublishBatchAsync(statusBatch, ct);
}

// Cost: 2 HTTP requests, 20 Table Storage writes (but more efficient)
// Savings: ~40% reduction in HTTP overhead
```

### 5. Configurable Ping Frequency

**Per-Device Ping Frequency:**
```csharp
public class Device
{
    // Allow different ping frequencies per device
    public int PingFrequencySeconds { get; set; } = 300; // Default 5 minutes
}

// In agent service
private bool ShouldPingDevice(Device device)
{
    var lastPing = _lastPingTimes.GetValueOrDefault(device.Id, DateTime.MinValue);
    var frequency = TimeSpan.FromSeconds(device.PingFrequencySeconds);
    return DateTime.UtcNow - lastPing >= frequency;
}

// Cost Impact:
// - Critical devices: 5 second ping = 17,280 pings/day
// - Normal devices: 300 second ping = 288 pings/day
// - Savings: 98% reduction for non-critical devices
```

## Cost Monitoring

### Application Insights Queries

**SQL Query Count:**
```kusto
dependencies
| where timestamp > ago(1h)
| where type == "SQL"
| summarize QueryCount = count(), AvgDuration = avg(duration) by bin(timestamp, 5m)
| render timechart
```

**Table Storage Operations:**
```kusto
dependencies
| where timestamp > ago(1h)
| where type == "Azure table"
| summarize Operations = count(), TotalCost = count() * 0.0000004 by name
| order by Operations desc
```

**Queue Storage Operations:**
```kusto
dependencies
| where timestamp > ago(1h)
| where type == "Azure queue"
| summarize Operations = count(), TotalCost = count() * 0.00004 by name
| order by Operations desc
```

**Cost Per Tenant:**
```kusto
requests
| where timestamp > ago(1d)
| extend tenantId = tostring(customDimensions.TenantId)
| summarize 
    Requests = count(),
    SqlQueries = countif(customDimensions.HasSql == "true"),
    TableOps = countif(customDimensions.TableOp == "true")
    by tenantId
| extend EstimatedCost = (SqlQueries * 0.0001) + (TableOps * 0.00004)
| order by EstimatedCost desc
```

### Custom Metrics

```csharp
public class CostTrackingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TelemetryClient _telemetry;
    
    public async Task InvokeAsync(HttpContext context)
    {
        var operationCost = 0.0;
        
        // Track SQL queries
        var sqlQueries = 0;
        context.Items["SqlQueryCount"] = 0;
        
        await _next(context);
        
        // Calculate cost
        sqlQueries = (int)(context.Items["SqlQueryCount"] ?? 0);
        operationCost += sqlQueries * 0.0001; // $0.0001 per SQL query
        
        // Track to Application Insights
        _telemetry.TrackMetric("OperationCost", operationCost);
        _telemetry.TrackMetric("SqlQueries", sqlQueries);
    }
}
```

## Cost Optimization Checklist

### For Every New Feature

- [ ] **Storage selection**: Use Table Storage unless SQL required
- [ ] **Query optimization**: Use partition keys, avoid full table scans
- [ ] **Idle detection**: Skip operations when system idle
- [ ] **Batch operations**: Group related operations
- [ ] **Caching**: Cache frequently accessed, rarely changed data
- [ ] **Monitoring**: Add cost tracking metrics
- [ ] **Testing**: Measure cost impact before deployment

### Monthly Cost Review

- [ ] **SQL DTU usage**: Trending down or stable?
- [ ] **Table Storage operations**: Within budget?
- [ ] **Queue Storage operations**: Optimized?
- [ ] **Idle time**: SQL idling during off-hours?
- [ ] **Per-tenant costs**: Any outliers?
- [ ] **Cost anomalies**: Any unexpected spikes?

## Cost-Effective Patterns

### Pattern 1: Write-Through Cache

```csharp
public class CachedDeviceStore : IDeviceStore
{
    private readonly IDeviceStore _tableStore;
    private readonly IMemoryCache _cache;
    
    public async Task<DeviceDto?> GetByIdAsync(Guid tenantId, Guid deviceId, CancellationToken ct)
    {
        var cacheKey = $"device:{tenantId}:{deviceId}";
        
        if (_cache.TryGetValue<DeviceDto>(cacheKey, out var cached))
            return cached;
        
        var device = await _tableStore.GetByIdAsync(tenantId, deviceId, ct);
        
        if (device != null)
        {
            _cache.Set(cacheKey, device, TimeSpan.FromMinutes(5));
        }
        
        return device;
    }
    
    public async Task UpsertAsync(Guid tenantId, Guid deviceId, string name, /* ... */, CancellationToken ct)
    {
        await _tableStore.UpsertAsync(tenantId, deviceId, name, /* ... */, ct);
        
        // Invalidate cache
        var cacheKey = $"device:{tenantId}:{deviceId}";
        _cache.Remove(cacheKey);
    }
}

// Cost Savings: Reduces Table Storage reads by 80% for frequently accessed devices
```

### Pattern 2: Lazy Loading

```csharp
// ❌ EXPENSIVE - Loads all data upfront
public async Task<DeviceDetailsDto> GetDeviceDetails(Guid deviceId)
{
    var device = await _db.Devices
        .Include(d => d.Actions)
        .Include(d => d.StatusHistory)
        .Include(d => d.Commands)
        .FirstOrDefaultAsync(d => d.Id == deviceId);
    
    return MapToDto(device);
}

// ✅ CHEAP - Loads data on demand
public async Task<DeviceDetailsDto> GetDeviceDetails(Guid deviceId)
{
    // Load basic device info only
    var device = await _db.Devices.FindAsync(deviceId);
    
    return new DeviceDetailsDto
    {
        Id = device.Id,
        Name = device.Name,
        // Actions loaded lazily via separate endpoint
        // StatusHistory loaded lazily via separate endpoint
    };
}
```

### Pattern 3: Aggregate and Archive

```csharp
public class DataArchivalService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Archive old data to cheaper storage
                await ArchiveOldDeviceStatusAsync(stoppingToken);
                
                // Wait 24 hours
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in archival service");
            }
        }
    }
    
    private async Task ArchiveOldDeviceStatusAsync(CancellationToken ct)
    {
        // Move data older than 30 days to Blob Storage (cheaper)
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        
        var oldRecords = await _db.DeviceStatusLogs
            .Where(s => s.Timestamp < cutoff)
            .ToListAsync(ct);
        
        if (!oldRecords.Any())
            return;
        
        // Write to Blob Storage (compressed JSON)
        var json = JsonSerializer.Serialize(oldRecords);
        var compressed = Compress(json);
        var blobName = $"archive/device-status-{cutoff:yyyy-MM}.json.gz";
        
        await _blobClient.UploadAsync(blobName, compressed, ct);
        
        // Delete from SQL
        _db.DeviceStatusLogs.RemoveRange(oldRecords);
        await _db.SaveChangesAsync(ct);
        
        _logger.LogInformation("Archived {Count} status records to {BlobName}", 
            oldRecords.Count, blobName);
    }
}

// Cost Savings:
// - SQL: $0.015/GB/month
// - Blob (Archive tier): $0.002/GB/month
// - Savings: 87% reduction for archived data
```

## Scaling Strategies

### Horizontal Scaling (Multi-Agent)

```
Cost per agent (monthly):
├── Table Storage reads: ~10,000 ops   = $0.04
├── Queue Storage polls:  ~25,920 ops  = $0.10
├── Status uploads:       ~8,640 ops   = $0.04
└── Total per agent:                    $0.18/month

10 agents = $1.80/month
100 agents = $18.00/month
```

**Key Insight**: Agent cost scales linearly and cheaply

### Vertical Scaling (Per-Tenant)

```
Cost per tenant (monthly):
├── Table Storage:        ~50,000 ops  = $0.20
├── Queue Storage:        ~10,000 ops  = $0.04
├── SQL (shared):         $0.50 (1/10th of Basic tier)
└── Total per tenant:                   $0.74/month

10 tenants = $7.40/month
100 tenants = $74.00/month
```

**Key Insight**: Multi-tenant sharing amortizes SQL costs

## Cost Optimization ROI

### Example: Medium Deployment

**Before optimization:**
```
Infrastructure:
- Azure SQL S1: $30/month
- Storage: $2/month
Total: $32/month

Operations:
- 10 agents polling SQL every 10s
- 5 users loading dashboard every 30s
- 20 devices reporting status every 5 minutes
- SQL DTU: 40% average, 80% peak
```

**After optimization:**
```
Infrastructure:
- Azure SQL Basic: $5/month (idle 80% of time)
- Table Storage: $0.50/month
- Queue Storage: $0.10/month
- Blob Storage: $2/month
Total: $7.60/month

Operations:
- Agents read from Table Storage
- Dashboard reads from Table Storage
- Commands via Queue Storage
- SQL DTU: <5% average, 20% peak
```

**ROI:**
- **Savings**: $24.40/month ($292.80/year)
- **Reduction**: 76%
- **Payback period**: Immediate (no additional cost)

## References

- [SQL Elimination Guide](../SQL_ELIMINATION_GUIDE.md)
- [Idle Detection Guide](../IDLE_DETECTION.md)
- [Azure Pricing Calculator](https://azure.microsoft.com/en-us/pricing/calculator/)
- [Table Storage Pricing](https://azure.microsoft.com/en-us/pricing/details/storage/tables/)
- [SQL Database Pricing](https://azure.microsoft.com/en-us/pricing/details/sql-database/)
