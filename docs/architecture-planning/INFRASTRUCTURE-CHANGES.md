# Infrastructure Changes - Architecture Planning Guide

## Overview

This document guides coding agents through infrastructure changes in ProdControlAV. The system uses Azure cloud services for scalability, cost optimization, and multi-tenant isolation.

## Core Infrastructure Principles

### 1. Cost Optimization Architecture
- **Azure Table Storage**: Primary storage for real-time operations (~$0.50/month vs $30/month SQL)
- **Azure Queue Storage**: Command delivery to agents (~$0.0004/10,000 operations)
- **Azure SQL Database**: Audit trails and administrative operations only
- **Idle detection**: Automatic suspension of SQL operations during inactivity
- **Pay-per-use**: Services scale down to zero cost when unused

### 2. Multi-Tenant Isolation
- **Partition Keys**: TenantId as partition key in Table Storage
- **Queue Separation**: Per-tenant, per-agent queues (`pcav-{tenantId}-{agentId}`)
- **Network Isolation**: API authentication prevents cross-tenant access
- **Data Segregation**: All queries filtered by TenantId

### 3. Service Reliability
- **Automatic Retries**: Exponential backoff for transient failures
- **Circuit Breakers**: Prevent cascading failures
- **Health Checks**: Monitor service availability
- **Graceful Degradation**: Continue operation when dependencies unavailable

## Azure Services Architecture

### Current Services

```
┌─────────────────────────────────────────────────────────────┐
│                     Azure Cloud Services                     │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│  ┌──────────────────┐  ┌──────────────────┐                │
│  │  Azure SQL DB    │  │  Table Storage   │                │
│  │  (Admin/Audit)   │  │  (Real-time)     │                │
│  └────────┬─────────┘  └────────┬─────────┘                │
│           │                      │                           │
│  ┌────────▼──────────────────────▼─────────┐                │
│  │        ProdControlAV.API               │                │
│  │     (ASP.NET Core Web API)              │                │
│  └────────┬────────────────────────────────┘                │
│           │                                                   │
│  ┌────────▼─────────┐  ┌──────────────────┐                │
│  │  Queue Storage   │  │  Blob Storage    │                │
│  │  (Commands)      │  │  (Static Files)  │                │
│  └────────┬─────────┘  └──────────────────┘                │
│           │                                                   │
└───────────┼───────────────────────────────────────────────────┘
            │
    ┌───────▼────────┐
    │  Agent (Pi)    │
    │  Device Monitoring│
    └────────────────┘
```

### Service Responsibilities

| Service | Purpose | Cost Optimization |
|---------|---------|-------------------|
| Azure SQL DB | Audit trails, admin operations | Idle detection, minimal queries |
| Table Storage | Device metadata, status, actions | Partitioned by TenantId, fast reads |
| Queue Storage | Agent command delivery | Pay-per-operation, auto-scaling |
| Blob Storage | Static assets, logs | CDN integration, lifecycle policies |

## Adding New Azure Services

### Decision Framework

**Ask these questions before adding a service:**
1. Can this be done with existing services?
2. What is the monthly cost at current scale?
3. Does it support multi-tenant isolation?
4. Can it scale down to zero during idle?
5. Is there a cheaper alternative (Table Storage vs Cosmos DB)?

### Process for Adding New Service

1. **Document Justification**
   - Why existing services can't meet the need
   - Cost estimate at various scales
   - Performance requirements
   - Security/compliance requirements

2. **Update Infrastructure as Code**
   - Add to ARM templates or Terraform
   - Configure with least-privilege access
   - Enable diagnostic logging
   - Set up alerts and monitoring

3. **Create Service Interface** in `src/ProdControlAV.Core/Interfaces/`
   ```csharp
   public interface INewService
   {
       Task<Result> PerformOperationAsync(Guid tenantId, Request request, CancellationToken ct);
   }
   ```

4. **Implement Service** in `src/ProdControlAV.Infrastructure/Services/`
   ```csharp
   public class NewService : INewService
   {
       private readonly ServiceClient _client;
       private readonly ILogger<NewService> _logger;
       
       public NewService(ServiceClient client, ILogger<NewService> logger)
       {
           _client = client;
           _logger = logger;
       }
       
       public async Task<Result> PerformOperationAsync(Guid tenantId, Request request, CancellationToken ct)
       {
           try
           {
               _logger.LogInformation("Performing operation for tenant {TenantId}", tenantId);
               
               // Implement with tenant isolation
               var result = await _client.ExecuteAsync(request, ct);
               
               return result;
           }
           catch (Exception ex)
           {
               _logger.LogError(ex, "Failed to perform operation for tenant {TenantId}", tenantId);
               throw;
           }
       }
   }
   ```

5. **Register in DI Container**
   ```csharp
   // In Program.cs
   builder.Services.AddSingleton<INewService>(sp =>
   {
       var connectionString = builder.Configuration["NewService:ConnectionString"];
       var client = new ServiceClient(connectionString);
       var logger = sp.GetRequiredService<ILogger<NewService>>();
       return new NewService(client, logger);
   });
   ```

6. **Add Configuration**
   ```json
   // appsettings.json
   {
     "NewService": {
       "ConnectionString": "...",
       "Options": {
         "Timeout": 30,
         "RetryCount": 3
       }
     }
   }
   ```

7. **Document Cost Impact**
   - Update cost estimates in deployment docs
   - Add monitoring for service usage
   - Configure alerts for cost thresholds

## Table Storage Patterns

### Standard Implementation Pattern

```csharp
public class TableYourEntityStore : IYourEntityStore
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly ILogger<TableYourEntityStore> _logger;
    private const string TableName = "YourEntities";
    
    public TableYourEntityStore(TableServiceClient tableServiceClient, ILogger<TableYourEntityStore> logger)
    {
        _tableServiceClient = tableServiceClient;
        _logger = logger;
    }
    
    // Always use TenantId as PartitionKey for isolation
    private static string GetPartitionKey(Guid tenantId) => tenantId.ToString();
    private static string GetRowKey(Guid entityId) => entityId.ToString();
    
    public async Task UpsertAsync(Guid tenantId, Guid entityId, string name, CancellationToken ct)
    {
        var table = _tableServiceClient.GetTableClient(TableName);
        await table.CreateIfNotExistsAsync(ct);
        
        var entity = new TableEntity(GetPartitionKey(tenantId), GetRowKey(entityId))
        {
            { "Name", name },
            { "UpdatedUtc", DateTimeOffset.UtcNow }
        };
        
        await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
        _logger.LogInformation("Upserted {TableName} entity {EntityId} for tenant {TenantId}", 
            TableName, entityId, tenantId);
    }
    
    public async IAsyncEnumerable<YourEntityDto> GetAllForTenantAsync(Guid tenantId, 
        [EnumeratorCancellation] CancellationToken ct)
    {
        var table = _tableServiceClient.GetTableClient(TableName);
        
        // Efficient query - single partition scan
        var filter = $"PartitionKey eq '{GetPartitionKey(tenantId)}'";
        
        await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter, cancellationToken: ct))
        {
            yield return MapToDto(entity);
        }
    }
    
    public async Task DeleteAsync(Guid tenantId, Guid entityId, CancellationToken ct)
    {
        var table = _tableServiceClient.GetTableClient(TableName);
        
        try
        {
            await table.DeleteEntityAsync(GetPartitionKey(tenantId), GetRowKey(entityId), 
                cancellationToken: ct);
            _logger.LogInformation("Deleted {TableName} entity {EntityId} for tenant {TenantId}", 
                TableName, entityId, tenantId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogWarning("Entity {EntityId} not found for deletion in {TableName}", 
                entityId, TableName);
        }
    }
}
```

### Best Practices

1. **Partition Key Design**
   - Always use TenantId as partition key
   - Enables efficient tenant-scoped queries
   - Provides natural multi-tenant isolation

2. **Row Key Design**
   - Use EntityId (Guid) as row key
   - Enables direct lookups by ID
   - Combine with PartitionKey for unique identifier

3. **Property Naming**
   - Use PascalCase for consistency
   - Avoid reserved names (Timestamp, PartitionKey, RowKey, ETag)
   - Keep property names under 255 characters

4. **Query Optimization**
   - Always filter by PartitionKey when possible
   - Use `$select` to limit returned properties
   - Implement pagination for large result sets

## Queue Storage Patterns

### Command Queue Implementation

```csharp
public class AgentCommandQueueService : IAgentCommandQueueService
{
    private readonly QueueServiceClient _queueServiceClient;
    private readonly ILogger<AgentCommandQueueService> _logger;
    
    // Queue naming: pcav-{tenantId}-{agentId}
    private static string GetQueueName(Guid tenantId, Guid agentId) 
        => $"pcav-{tenantId}-{agentId}".ToLowerInvariant();
    
    public async Task EnqueueCommandAsync(Guid tenantId, Guid agentId, AgentCommand command, CancellationToken ct)
    {
        var queueName = GetQueueName(tenantId, agentId);
        var queue = _queueServiceClient.GetQueueClient(queueName);
        await queue.CreateIfNotExistsAsync(cancellationToken: ct);
        
        var messageJson = JsonSerializer.Serialize(command);
        var messageBytes = Encoding.UTF8.GetBytes(messageJson);
        var messageBase64 = Convert.ToBase64String(messageBytes);
        
        // Scheduled execution support
        TimeSpan? visibilityTimeout = command.DueUtc.HasValue 
            ? command.DueUtc.Value - DateTimeOffset.UtcNow 
            : null;
        
        await queue.SendMessageAsync(messageBase64, visibilityTimeout: visibilityTimeout, cancellationToken: ct);
        
        _logger.LogInformation("Enqueued command {CommandId} to queue {QueueName}", 
            command.Id, queueName);
    }
    
    public async Task<AgentCommand?> ReceiveCommandAsync(Guid tenantId, Guid agentId, CancellationToken ct)
    {
        var queueName = GetQueueName(tenantId, agentId);
        var queue = _queueServiceClient.GetQueueClient(queueName);
        
        // 60-second visibility timeout
        var response = await queue.ReceiveMessageAsync(TimeSpan.FromSeconds(60), ct);
        
        if (response.Value == null)
            return null;
        
        var message = response.Value;
        
        // Check for poison messages (5+ dequeue attempts)
        if (message.DequeueCount >= 5)
        {
            await HandlePoisonMessageAsync(tenantId, agentId, message, ct);
            return null;
        }
        
        var messageJson = Encoding.UTF8.GetString(Convert.FromBase64String(message.MessageText));
        var command = JsonSerializer.Deserialize<AgentCommand>(messageJson);
        
        return command;
    }
    
    public async Task AcknowledgeCommandAsync(Guid tenantId, Guid agentId, string messageId, 
        string popReceipt, CancellationToken ct)
    {
        var queueName = GetQueueName(tenantId, agentId);
        var queue = _queueServiceClient.GetQueueClient(queueName);
        
        await queue.DeleteMessageAsync(messageId, popReceipt, ct);
        
        _logger.LogInformation("Acknowledged and deleted command message {MessageId} from queue {QueueName}", 
            messageId, queueName);
    }
}
```

### Queue Best Practices

1. **Queue Naming**
   - Use lowercase names
   - Include tenant and agent IDs for isolation
   - Format: `pcav-{tenantId}-{agentId}`

2. **Message Format**
   - Serialize to JSON
   - Base64 encode for queue compatibility
   - Include message version for future changes

3. **Visibility Timeout**
   - 60 seconds for command execution
   - Allows retry on agent failure
   - Prevents message loss

4. **Poison Message Handling**
   - Move to poison queue after 5 attempts
   - Log and alert on poison messages
   - Manual intervention required

## Service Configuration

### Connection Strings

```csharp
// Preferred: Use connection strings for development
builder.Services.AddSingleton(sp =>
{
    var connectionString = builder.Configuration["Storage:ConnectionString"];
    return new TableServiceClient(connectionString);
});

// Production: Use Managed Identity (no connection strings)
builder.Services.AddSingleton(sp =>
{
    var endpoint = new Uri(builder.Configuration["Storage:TablesEndpoint"]);
    return new TableServiceClient(endpoint, new DefaultAzureCredential());
});
```

### Environment-Specific Configuration

```json
// appsettings.Development.json
{
  "Storage": {
    "ConnectionString": "UseDevelopmentStorage=true"  // Azurite
  }
}

// appsettings.Production.json
{
  "Storage": {
    "TablesEndpoint": "https://prodcontrolav.table.core.windows.net",
    "UseManagedIdentity": true
  }
}
```

## Background Services

### Hosted Service Pattern

```csharp
public class YourBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IActivityMonitor _activityMonitor;
    private readonly ILogger<YourBackgroundService> _logger;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(30);
    
    public YourBackgroundService(IServiceProvider serviceProvider, 
        IActivityMonitor activityMonitor, ILogger<YourBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _activityMonitor = activityMonitor;
        _logger = logger;
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background service started");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Check if system is idle to avoid SQL queries
                if (await _activityMonitor.IsSystemIdleAsync(stoppingToken))
                {
                    _logger.LogDebug("System is idle, skipping work cycle");
                    await Task.Delay(_pollingInterval, stoppingToken);
                    continue;
                }
                
                // Create scope for scoped services
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IYourService>();
                
                await service.DoWorkAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in background service work cycle");
            }
            
            await Task.Delay(_pollingInterval, stoppingToken);
        }
        
        _logger.LogInformation("Background service stopped");
    }
}
```

### Best Practices for Background Services

1. **Idle Detection**
   - Always check `IActivityMonitor.IsSystemIdleAsync()`
   - Skip SQL operations when system is idle
   - Allows database to scale down

2. **Error Handling**
   - Catch and log all exceptions
   - Don't crash the service
   - Implement exponential backoff for retries

3. **Scoped Services**
   - Create scope for each work cycle
   - Dispose resources properly
   - Avoid memory leaks

4. **Graceful Shutdown**
   - Respect `CancellationToken`
   - Complete current work before stopping
   - Log start and stop events

## Monitoring and Observability

### Structured Logging

```csharp
_logger.LogInformation(
    "Processed {Count} items for tenant {TenantId} in {Duration}ms",
    count, tenantId, stopwatch.ElapsedMilliseconds);
```

### Application Insights Integration

```csharp
// Track custom metrics
_telemetryClient.TrackMetric("TableStorage.ReadLatency", latencyMs);
_telemetryClient.TrackMetric("QueueStorage.MessageCount", messageCount);

// Track dependencies
using var operation = _telemetryClient.StartOperation<DependencyTelemetry>("TableStorage");
operation.Telemetry.Type = "Azure table";
operation.Telemetry.Data = "GetAllForTenant";
try
{
    await _tableStore.GetAllForTenantAsync(tenantId, ct);
    operation.Telemetry.Success = true;
}
catch
{
    operation.Telemetry.Success = false;
    throw;
}
```

### Health Checks

```csharp
// In Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<TableStorageHealthCheck>("table_storage")
    .AddCheck<QueueStorageHealthCheck>("queue_storage")
    .AddCheck<SqlDatabaseHealthCheck>("sql_database");

// Health check endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

## Security Considerations

### Managed Identity (Production)

```csharp
// No connection strings in production
var credential = new DefaultAzureCredential();
var tableClient = new TableServiceClient(new Uri(endpoint), credential);
var queueClient = new QueueServiceClient(new Uri(endpoint), credential);
```

### Connection String (Development Only)

```csharp
// Only for local development with Azurite
var connectionString = "UseDevelopmentStorage=true";
var tableClient = new TableServiceClient(connectionString);
```

### Least Privilege Access

- Storage Account: Assign "Storage Table Data Contributor" role
- Queue Storage: Assign "Storage Queue Data Contributor" role
- SQL Database: Use managed identity with minimal permissions

## Performance Optimization

### Table Storage

- **Batch Operations**: Use `TableTransactionAction` for multiple writes
- **Parallel Queries**: Query multiple partitions in parallel
- **Property Projection**: Use `$select` to limit data transfer
- **Caching**: Cache frequently accessed, rarely changed data

### Queue Storage

- **Batch Receive**: Process multiple messages in parallel
- **Prefetch Messages**: Receive next message while processing current
- **Dead Letter Queue**: Move failed messages to poison queue
- **Message Compression**: Compress large payloads

### SQL Database

- **Connection Pooling**: Enabled by default in EF Core
- **Query Optimization**: Use indexes, avoid N+1 queries
- **Batch Operations**: Use `AddRange()` instead of multiple `Add()` calls
- **Idle Detection**: Suspend queries during inactivity

## Testing Infrastructure Changes

### Unit Tests
```csharp
[Fact]
public async Task UpsertAsync_WritesToTableStorage()
{
    // Arrange
    var mockTableClient = new Mock<TableClient>();
    var store = new TableYourEntityStore(mockTableClient.Object, _logger);
    
    // Act
    await store.UpsertAsync(tenantId, entityId, "TestName", CancellationToken.None);
    
    // Assert
    mockTableClient.Verify(t => t.UpsertEntityAsync(
        It.IsAny<TableEntity>(), 
        TableUpdateMode.Replace, 
        CancellationToken.None), Times.Once);
}
```

### Integration Tests with Azurite
```bash
# Start Azurite for local testing
docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 \
    mcr.microsoft.com/azure-storage/azurite
```

```csharp
[Fact]
public async Task IntegrationTest_WithAzurite()
{
    // Arrange
    var connectionString = "UseDevelopmentStorage=true";
    var tableClient = new TableServiceClient(connectionString);
    var store = new TableYourEntityStore(tableClient, _logger);
    
    // Act
    await store.UpsertAsync(tenantId, entityId, "TestName", CancellationToken.None);
    var result = await store.GetByIdAsync(tenantId, entityId, CancellationToken.None);
    
    // Assert
    Assert.NotNull(result);
    Assert.Equal("TestName", result.Name);
}
```

## Deployment Checklist

Before deploying infrastructure changes:

- [ ] Azure resources created (Storage Account, SQL DB, etc.)
- [ ] Managed Identity configured for production
- [ ] Connection strings secured in Key Vault
- [ ] Health checks implemented
- [ ] Monitoring and alerts configured
- [ ] Cost estimates documented
- [ ] Multi-tenant isolation verified
- [ ] Performance tested under load
- [ ] Rollback plan documented
- [ ] Documentation updated

## References

- [SQL Elimination Guide](../SQL_ELIMINATION_GUIDE.md)
- [Queue Commands](../QUEUE_COMMANDS.md)
- [Azure Table Storage Quickstart](../../AZURE-TABLE-QUICKSTART.md)
- [Deployment Guide](../../DEPLOYMENT.md)
