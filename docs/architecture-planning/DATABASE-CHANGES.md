# Database Changes - Architecture Planning Guide

## Overview

This document provides comprehensive guidance for coding agents implementing database changes in ProdControlAV. The system uses a **hybrid storage architecture** with Azure SQL Database for authoritative records and Azure Table Storage for real-time operations.

## Core Principles

### 1. Cost Optimization First
- **Azure Table Storage is preferred** over SQL Database for all real-time operations
- **SQL Database is only for**: Administrative operations, audit trails, and historical reporting
- **Never query SQL during normal operations**: Dashboard loads, agent monitoring, status updates
- **Idle detection**: System automatically suspends SQL operations when no users are active (10-minute idle timeout)

### 2. Multi-Tenant Architecture
- **Every table MUST have TenantId** as a foreign key to the `Tenants` table
- **All queries MUST filter by TenantId** using the injected `ITenantProvider` service
- **Partition keys in Table Storage** use TenantId for isolation
- **Never expose data across tenants** - always validate tenant ownership

### 3. Dual-Write Pattern
- **SQL is the source of truth** for administrative data
- **Table Storage is read source** for real-time operations
- **Synchronization**: Write to SQL first, then immediately write to Table Storage (no Outbox pattern anymore)
- **Error handling**: Log Table Storage failures but don't block SQL operations

## When to Add a New Table

### Use SQL Database When:
- Data requires ACID transactions (financial records, audit logs)
- Complex relational queries are needed (reporting, analytics)
- Data has low update frequency (configuration, settings)
- Historical data for compliance (audit trails, user actions)

### Use Azure Table Storage When:
- High-frequency reads (dashboard data, device status)
- Real-time monitoring data (agent heartbeats, device pings)
- Simple key-value lookups (device by ID, status by device ID)
- No complex joins required (denormalized data is acceptable)

### Decision Matrix

| Scenario | Storage | Rationale |
|----------|---------|-----------|
| Device configuration | SQL + Table | SQL for audit, Table for fast reads |
| Device status | Table only | High frequency, no historical audit needed |
| User accounts | SQL only | Low frequency, requires strong consistency |
| Agent commands | SQL + Queue | SQL for audit, Queue for delivery |
| Command history | SQL only | Historical reporting and compliance |
| Device actions | SQL + Table | SQL for audit, Table for fast reads |
| Activity logs | Table only | High frequency, eventual consistency OK |

## Adding a New SQL Table

### Step-by-Step Process

1. **Define the Entity Model** in `src/ProdControlAV.Core/Models/`
   ```csharp
   namespace ProdControlAV.Core.Models;
   
   public class YourEntity
   {
       public Guid Id { get; set; }
       public Guid TenantId { get; set; }  // REQUIRED for multi-tenant
       public string Name { get; set; } = "";
       public DateTimeOffset CreatedUtc { get; set; }
       public DateTimeOffset? UpdatedUtc { get; set; }
       
       // Navigation properties
       public Tenant Tenant { get; set; } = null!;
   }
   ```

2. **Add to DbContext** in `src/ProdControlAV.API/Data/AppDbContext.cs`
   ```csharp
   public DbSet<YourEntity> YourEntities { get; set; }
   
   protected override void OnModelCreating(ModelBuilder modelBuilder)
   {
       // Configure entity
       modelBuilder.Entity<YourEntity>(entity =>
       {
           entity.HasKey(e => e.Id);
           entity.HasIndex(e => e.TenantId);
           entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
           
           // Multi-tenant foreign key
           entity.HasOne(e => e.Tenant)
               .WithMany()
               .HasForeignKey(e => e.TenantId)
               .OnDelete(DeleteBehavior.Cascade);
       });
   }
   ```

3. **Create Migration**
   ```bash
   cd src/ProdControlAV.API
   dotnet ef migrations add AddYourEntityTable
   ```

4. **Review Migration File**
   - Verify TenantId foreign key exists
   - Ensure indexes are created on TenantId
   - Check for any sensitive data that needs encryption

5. **Test Migration Locally**
   ```bash
   dotnet ef database update
   ```

6. **Update Deployment Script** in `DATABASE-DEPLOYMENT.md`
   - Add migration to deployment checklist
   - Document any manual data migration steps

## Adding a New Table Storage Table

### Step-by-Step Process

1. **Define Table Entity Interface** in `src/ProdControlAV.Core/Interfaces/`
   ```csharp
   public interface IYourEntityStore
   {
       Task UpsertAsync(Guid tenantId, Guid entityId, string name, /* other fields */, CancellationToken ct);
       Task DeleteAsync(Guid tenantId, Guid entityId, CancellationToken ct);
       IAsyncEnumerable<YourEntityDto> GetAllForTenantAsync(Guid tenantId, CancellationToken ct);
       Task<YourEntityDto?> GetByIdAsync(Guid tenantId, Guid entityId, CancellationToken ct);
   }
   
   public record YourEntityDto(Guid Id, Guid TenantId, string Name /* other fields */);
   ```

2. **Implement Store** in `src/ProdControlAV.Infrastructure/Services/`
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
       
       public async Task UpsertAsync(Guid tenantId, Guid entityId, string name, CancellationToken ct)
       {
           var table = _tableServiceClient.GetTableClient(TableName);
           await table.CreateIfNotExistsAsync(ct);
           
           var entity = new TableEntity(tenantId.ToString(), entityId.ToString())
           {
               { "Name", name },
               { "UpdatedUtc", DateTimeOffset.UtcNow }
           };
           
           await table.UpsertEntityAsync(entity, TableUpdateMode.Replace, ct);
           _logger.LogInformation("Upserted entity {EntityId} for tenant {TenantId} to Table Storage", entityId, tenantId);
       }
       
       public async IAsyncEnumerable<YourEntityDto> GetAllForTenantAsync(Guid tenantId, [EnumeratorCancellation] CancellationToken ct)
       {
           var table = _tableServiceClient.GetTableClient(TableName);
           
           // Partition key = TenantId for efficient queries
           await foreach (var entity in table.QueryAsync<TableEntity>(
               filter: $"PartitionKey eq '{tenantId}'",
               cancellationToken: ct))
           {
               yield return new YourEntityDto(
                   Guid.Parse(entity.RowKey),
                   Guid.Parse(entity.PartitionKey),
                   entity.GetString("Name") ?? ""
               );
           }
       }
   }
   ```

3. **Register in DI** in `src/ProdControlAV.API/Program.cs`
   ```csharp
   builder.Services.AddSingleton<IYourEntityStore, TableYourEntityStore>();
   ```

4. **Create Table in Azure**
   ```bash
   # Azure CLI
   az storage table create --name YourEntities --account-name your-storage-account
   
   # Or use Azure Portal: Storage Account → Tables → + Table
   ```

5. **Document in Deployment Guide**
   - Add to `AZURE-TABLE-QUICKSTART.md`
   - List table creation steps

## Adding a New Column to Existing SQL Table

### Step-by-Step Process

1. **Update Entity Model** in `src/ProdControlAV.Core/Models/`
   ```csharp
   public class Device
   {
       // Existing properties...
       
       // New property with sensible default
       public int NewProperty { get; set; } = 100;
   }
   ```

2. **Create Migration**
   ```bash
   cd src/ProdControlAV.API
   dotnet ef migrations add AddNewPropertyToDevice
   ```

3. **Review Migration for Safety**
   - Nullable columns are safer (no default required)
   - Non-nullable columns should have defaults
   - Check for performance impact on large tables
   
4. **If Adding to Table Storage Entity**
   - Update `IDeviceStore.UpsertAsync()` signature
   - Add field to Table Storage entity
   - Update all callers (API controllers, background services)
   - **Important**: Table Storage is schema-less, new fields are automatically handled

5. **Test Migration**
   ```bash
   dotnet ef database update
   # Verify with: dotnet build && dotnet test
   ```

## Multi-Tenant Queries

### Always Use Tenant Provider

**Good Pattern:**
```csharp
public class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantProvider _tenant;
    
    public async Task<ActionResult<List<Device>>> GetDevices()
    {
        var devices = await _db.Devices
            .Where(d => d.TenantId == _tenant.TenantId)
            .ToListAsync();
        return Ok(devices);
    }
}
```

**Bad Pattern (Security Risk):**
```csharp
// ❌ NEVER DO THIS - exposes all tenant data
var devices = await _db.Devices.ToListAsync();
```

### Table Storage Multi-Tenant Queries

**Use PartitionKey = TenantId:**
```csharp
// Efficient - queries single partition
var filter = $"PartitionKey eq '{tenantId}'";
await foreach (var entity in table.QueryAsync<TableEntity>(filter: filter, ct))
{
    // Process entities
}
```

**Never scan entire table:**
```csharp
// ❌ EXPENSIVE - scans all partitions
await foreach (var entity in table.QueryAsync<TableEntity>(ct))
{
    // This costs money and is slow
}
```

## Data Migration Strategies

### Migrating from SQL to Table Storage

When moving data from SQL to Table Storage:

1. **Create Backfill Script**
   ```csharp
   // Example: Migrate device status to Table Storage
   public async Task BackfillDeviceStatus()
   {
       var devices = await _db.Devices.ToListAsync();
       foreach (var device in devices)
       {
           await _deviceStore.UpsertAsync(
               device.TenantId,
               device.Id,
               device.Name,
               device.Ip,
               device.Type,
               DateTimeOffset.UtcNow,
               CancellationToken.None
           );
       }
   }
   ```

2. **Run as One-Time Script**
   - Create console app in `scripts/` directory
   - Test locally first
   - Run in production during maintenance window

3. **Verify Data Integrity**
   - Compare SQL count vs Table Storage count
   - Spot check critical records
   - Monitor for errors in application logs

### Removing SQL Table (After Migration to Table Storage)

**DO NOT remove SQL tables unless:**
- Table Storage has been proven stable for 30+ days
- All audit requirements are met
- Backup/export of historical data is complete

**When removing:**
1. Mark table as deprecated (add comment to model)
2. Stop writing to SQL (keep reading for validation)
3. Monitor for 2 weeks
4. Create final backup
5. Drop table via migration

## Common Pitfalls

### ❌ Avoid These Anti-Patterns

1. **Querying SQL during agent operations**
   ```csharp
   // ❌ BAD - agent polls every 10 seconds
   var commands = await _db.AgentCommands.Where(c => c.AgentId == agentId).ToListAsync();
   ```
   
   **Fix**: Use Azure Queue Storage for commands
   ```csharp
   // ✅ GOOD
   var command = await _commandQueue.ReceiveAsync(agentId, ct);
   ```

2. **Not filtering by TenantId**
   ```csharp
   // ❌ BAD - security vulnerability
   var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId);
   ```
   
   **Fix**: Always filter by tenant
   ```csharp
   // ✅ GOOD
   var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.TenantId == _tenant.TenantId);
   ```

3. **Writing to SQL during high-frequency operations**
   ```csharp
   // ❌ BAD - status updates every 5 seconds
   device.Status = isOnline;
   await _db.SaveChangesAsync();
   ```
   
   **Fix**: Write to Table Storage only
   ```csharp
   // ✅ GOOD
   await _deviceStatusStore.UpsertAsync(tenantId, deviceId, "ONLINE", ct);
   ```

4. **Not handling Table Storage write failures**
   ```csharp
   // ❌ BAD - failure blocks entire operation
   await _db.SaveChangesAsync();
   await _deviceStore.UpsertAsync(...); // If this fails, SQL already committed
   ```
   
   **Fix**: Log and continue
   ```csharp
   // ✅ GOOD
   await _db.SaveChangesAsync();
   try
   {
       await _deviceStore.UpsertAsync(...);
   }
   catch (Exception ex)
   {
       _logger.LogError(ex, "Failed to write to Table Storage, but SQL succeeded");
   }
   ```

## Testing Requirements

### Unit Tests
- Mock `DbContext` and Table Storage clients
- Test tenant filtering logic
- Verify multi-tenant isolation
- Test with multiple tenants in same test

### Integration Tests
- Use in-memory database or test database
- Use Azurite for Table Storage testing
- Verify end-to-end data flow (SQL → Table Storage)
- Test migration scripts

### Test Example
```csharp
[Fact]
public async Task GetDevices_FiltersToCurrentTenant()
{
    // Arrange
    var tenant1Id = Guid.NewGuid();
    var tenant2Id = Guid.NewGuid();
    
    _db.Devices.AddRange(
        new Device { Id = Guid.NewGuid(), TenantId = tenant1Id, Name = "Device1" },
        new Device { Id = Guid.NewGuid(), TenantId = tenant2Id, Name = "Device2" }
    );
    await _db.SaveChangesAsync();
    
    _mockTenantProvider.Setup(t => t.TenantId).Returns(tenant1Id);
    
    // Act
    var result = await _controller.GetDevices();
    
    // Assert
    var devices = result.Value;
    Assert.Single(devices);
    Assert.Equal("Device1", devices[0].Name);
}
```

## Security Considerations

### SQL Injection Prevention
- **Always use parameterized queries** (EF Core does this automatically)
- **Never concatenate user input** into SQL strings
- Use `.FromSqlInterpolated()` for raw SQL if needed

### Sensitive Data
- **Never log sensitive data** (passwords, API keys, personal data)
- **Use encryption at rest** for sensitive columns (Azure SQL TDE enabled by default)
- **Consider column-level encryption** for highly sensitive data

### Access Control
- **All endpoints require authentication** (cookie or JWT)
- **Tenant validation on every request** via middleware
- **Role-based authorization** for admin operations

## Performance Optimization

### Indexing Strategy
- **Primary keys**: Clustered index on `Id`
- **Tenant filtering**: Non-clustered index on `TenantId`
- **Foreign keys**: Automatically indexed
- **Query optimization**: Add indexes for WHERE/ORDER BY columns

### Query Optimization
```csharp
// ✅ GOOD - loads only needed columns
var devices = await _db.Devices
    .Where(d => d.TenantId == tenantId)
    .Select(d => new { d.Id, d.Name, d.Status })
    .ToListAsync();

// ❌ BAD - loads entire entity with all navigation properties
var devices = await _db.Devices
    .Include(d => d.Tenant)
    .Include(d => d.Actions)
    .ToListAsync();
```

### Table Storage Performance
- **Use PartitionKey efficiently**: TenantId as partition key
- **Keep RowKey simple**: EntityId as row key
- **Denormalize data**: Avoid joins by duplicating data
- **Batch operations**: Use `TableTransactionAction` for multiple writes

## Monitoring and Alerting

### Key Metrics
- **SQL DTU usage**: Should be <10 DTU during idle periods
- **Table Storage operations**: Track reads/writes per minute
- **Query latency**: P95 should be <100ms for Table Storage
- **Error rates**: Monitor storage operation failures

### Logging
```csharp
_logger.LogInformation("Queried {Count} devices for tenant {TenantId} from Table Storage in {Duration}ms", 
    count, tenantId, stopwatch.ElapsedMilliseconds);
```

## Deployment Checklist

Before deploying database changes:

- [ ] Migration tested locally
- [ ] Table Storage tables created in Azure
- [ ] Multi-tenant filtering verified
- [ ] Performance tested with realistic data volume
- [ ] Indexes added for new query patterns
- [ ] Error handling implemented
- [ ] Logging added for observability
- [ ] Documentation updated (this file, deployment docs)
- [ ] Rollback plan documented
- [ ] Backup taken before production deployment
- [ ] Create Pro OR Post publish SQL script for manual database updates

## References

- [SQL Elimination Guide](../SQL_ELIMINATION_GUIDE.md) - Detailed SQL elimination strategy
- [Azure Table Storage Quickstart](../../AZURE-TABLE-QUICKSTART.md) - Table Storage setup
- [Database Deployment Guide](../../DATABASE-DEPLOYMENT.md) - Production deployment process
- [Idle Detection Guide](../IDLE_DETECTION.md) - Idle detection and SQL suspension
