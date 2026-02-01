# Architecture Planning Documents - Index

## Overview

This directory contains comprehensive architecture planning documents for coding agents working on ProdControlAV. These documents establish patterns, best practices, and decision-making frameworks to ensure consistency, cost-effectiveness, security, and multi-tenant support across all changes.

## Purpose

These planning documents serve as:
- **First reference** for coding agents before implementing any changes
- **Decision frameworks** for choosing between implementation approaches
- **Pattern libraries** showcasing established code patterns
- **Checklists** ensuring all critical concerns are addressed
- **Cost guides** for optimizing infrastructure spending
- **Security baselines** for protecting multi-tenant data

## Document Structure

### Core Planning Documents

#### 1. [Database Changes](./DATABASE-CHANGES.md)
**When to use**: Adding/modifying SQL tables, Table Storage entities, or data models

**Covers**:
- SQL Database vs Table Storage decision matrix
- Multi-tenant data isolation patterns
- Entity Framework migrations
- Table Storage partitioning strategies
- Data synchronization patterns (SQL → Table Storage)
- Query optimization and indexing
- Testing database changes

**Key Principles**:
- Azure Table Storage preferred for real-time operations
- SQL Database for audit trails and administrative operations only
- Every table MUST have TenantId for multi-tenant isolation
- All queries MUST filter by TenantId
- Test with multiple tenants to verify isolation

#### 2. [Infrastructure Changes](./INFRASTRUCTURE-CHANGES.md)
**When to use**: Adding Azure services, background services, or infrastructure components

**Covers**:
- Azure services architecture and responsibilities
- Table Storage implementation patterns
- Queue Storage for command delivery
- Background service development
- Service configuration and connection management
- Health checks and monitoring
- Performance optimization

**Key Principles**:
- Cost optimization in service selection
- Multi-tenant isolation at infrastructure level
- Idle detection to reduce costs
- Managed Identity for production (no connection strings)
- Graceful degradation and retry logic

#### 3. [UI/UX Changes](./UI-UX-CHANGES.md)
**When to use**: Adding/modifying Blazor WebAssembly frontend components or pages

**Covers**:
- Blazor component architecture
- API communication patterns
- Real-time updates and auto-refresh
- Form handling and validation
- Modal dialogs and interactions
- Responsive design patterns
- Performance optimization (lazy loading, virtualization)
- Accessibility considerations

**Key Principles**:
- Minimize API calls, use caching
- All dashboard reads from Table Storage (not SQL)
- Real-time updates every 30 seconds
- Mobile-first responsive design
- Loading states and error handling

#### 4. [Agent Service Changes](./AGENT-SERVICE-CHANGES.md)
**When to use**: Modifying the Raspberry Pi agent service or adding device monitoring capabilities

**Covers**:
- Agent service architecture and lifecycle
- Device monitoring patterns
- Command execution and queue polling
- JWT authentication and token management
- Network operations (ICMP, TCP probing)
- Error handling and retry logic
- Deployment to Raspberry Pi

**Key Principles**:
- Queue-based command polling (no SQL queries)
- Table Storage for device lists and status uploads
- JWT authentication with automatic token refresh
- Efficient resource usage (low CPU/memory)
- Graceful degradation when API unavailable

#### 5. [Security & Multi-Tenant](./SECURITY-MULTITENANT.md)
**When to use**: **Every change** - security is always a concern

**Covers**:
- Multi-tenant isolation architecture
- Authentication mechanisms (cookies, JWT, API keys)
- Authorization policies and RBAC
- Input validation and injection prevention
- Secrets management (Azure Key Vault)
- Data encryption (at rest and in transit)
- Audit logging
- Security testing

**Key Principles**:
- Defense in depth - multiple security layers
- Every query MUST filter by TenantId
- HTTPS only, no exceptions
- Never log sensitive data (passwords, API keys)
- Strong JWT secrets (256+ bits)
- Fail secure - default to deny

#### 6. [Cost Optimization](./COST-OPTIMIZATION.md)
**When to use**: **Every change** - cost efficiency is a core concern

**Covers**:
- Storage hierarchy and cost comparison
- SQL elimination strategies
- Idle detection implementation
- Batch operations
- Per-device ping frequency configuration
- Cost monitoring with Application Insights
- Scaling strategies

**Key Principles**:
- Azure Table Storage preferred (10-100x cheaper than SQL)
- Queue Storage for command delivery
- Idle detection suspends SQL operations when inactive
- Monitor cost per tenant
- SQL Database only when users are logged in (ideal state)

## How to Use These Documents

### For New Features

1. **Identify change type(s)**:
   - Database changes? → Read [DATABASE-CHANGES.md](./DATABASE-CHANGES.md)
   - New Azure service? → Read [INFRASTRUCTURE-CHANGES.md](./INFRASTRUCTURE-CHANGES.md)
   - UI component? → Read [UI-UX-CHANGES.md](./UI-UX-CHANGES.md)
   - Agent modification? → Read [AGENT-SERVICE-CHANGES.md](./AGENT-SERVICE-CHANGES.md)

2. **Review security implications**: Always read [SECURITY-MULTITENANT.md](./SECURITY-MULTITENANT.md)

3. **Evaluate cost impact**: Always read [COST-OPTIMIZATION.md](./COST-OPTIMIZATION.md)

4. **Follow established patterns**: Use code examples from relevant planning docs

5. **Complete checklists**: Each document has deployment checklists

### Example Workflow: Adding a New Column

**Task**: Add `LastMaintenanceDate` column to `Devices` table

**Steps**:
1. Read [DATABASE-CHANGES.md](./DATABASE-CHANGES.md) - "Adding a New Column to Existing SQL Table"
2. Update `Device` entity model with new property
3. Create EF migration
4. Update Table Storage entity (schema-less, automatic)
5. Update API endpoints to include new field
6. Update UI components to display/edit field
7. Review [SECURITY-MULTITENANT.md](./SECURITY-MULTITENANT.md) - ensure tenant filtering maintained
8. Review [COST-OPTIMIZATION.md](./COST-OPTIMIZATION.md) - no additional cost (Table Storage is schema-less)
9. Test with multiple tenants
10. Deploy following checklist

### Example Workflow: Adding a New Background Service

**Task**: Add automated device health check background service

**Steps**:
1. Read [INFRASTRUCTURE-CHANGES.md](./INFRASTRUCTURE-CHANGES.md) - "Background Services"
2. Implement service with idle detection support
3. Read [COST-OPTIMIZATION.md](./COST-OPTIMIZATION.md) - ensure service suspends when idle
4. Read [SECURITY-MULTITENANT.md](./SECURITY-MULTITENANT.md) - ensure tenant isolation
5. Register service in DI container
6. Add configuration section to appsettings.json
7. Add health check endpoint
8. Test idle detection behavior
9. Deploy following checklist

### Example Workflow: Adding a New Device Type

**Task**: Add support for monitoring PTZ cameras

**Steps**:
1. Read [AGENT-SERVICE-CHANGES.md](./AGENT-SERVICE-CHANGES.md) - "Adding Custom Device Monitoring"
2. Update `Device` model with PTZ-specific fields (read [DATABASE-CHANGES.md](./DATABASE-CHANGES.md))
3. Implement PTZ-specific monitoring logic in agent
4. Update UI to display PTZ-specific controls (read [UI-UX-CHANGES.md](./UI-UX-CHANGES.md))
5. Ensure multi-tenant isolation (read [SECURITY-MULTITENANT.md](./SECURITY-MULTITENANT.md))
6. Monitor cost impact (read [COST-OPTIMIZATION.md](./COST-OPTIMIZATION.md))
7. Test end-to-end: device creation, monitoring, display
8. Deploy agent and API updates

## Critical Architectural Principles

These principles apply to **ALL** changes:

### 1. Cost Optimization First
- **Use Azure Table Storage** for all real-time operations
- **Use Azure Queue Storage** for command delivery
- **Use SQL Database** only for audit trails and administrative operations
- **Implement idle detection** to suspend SQL operations when inactive
- **Target**: SQL queries only when users are actively logged in

### 2. Multi-Tenant Isolation Always
- **Every table has TenantId** as a foreign key
- **Every query filters by TenantId** using injected `ITenantProvider`
- **Table Storage partitions by TenantId** for performance and isolation
- **Queue names include TenantId** for isolation
- **Test with multiple tenants** to verify no data leakage

### 3. Security by Default
- **HTTPS only** - all communication encrypted
- **JWT authentication for agents** - time-limited tokens
- **Cookie authentication for users** - secure, HTTP-only, SameSite
- **Input validation** on all user input
- **Output encoding** to prevent XSS
- **Never log secrets** (passwords, API keys, tokens)

### 4. Real-Time Performance
- **Dashboard loads from Table Storage** - not SQL
- **Agent device lists from Table Storage** - not SQL
- **Agent command polling from Queue Storage** - not SQL
- **Status updates to Table Storage** - not SQL
- **SQL queries <100/hour during normal operations**

### 5. Reliability and Resilience
- **Automatic retries** with exponential backoff
- **Circuit breakers** to prevent cascading failures
- **Graceful degradation** when dependencies unavailable
- **Health checks** for monitoring
- **Comprehensive logging** for troubleshooting

## Common Scenarios

### Scenario: Add new feature requiring database table

**Question**: Should I use SQL or Table Storage?

**Decision Tree**:
1. Is this real-time data accessed frequently? → **Table Storage**
2. Does it require complex joins or transactions? → **SQL** (+ project to Table Storage)
3. Is it configuration/admin data changed rarely? → **SQL only**
4. Is it audit trail for compliance? → **SQL only**

**Example**: New device status history
- High frequency reads (dashboard polls every 30s)
- No complex joins needed
- Eventual consistency acceptable
- **Decision**: Table Storage only

### Scenario: Add new API endpoint

**Question**: Should it query SQL or Table Storage?

**Decision Tree**:
1. Is this for real-time dashboard/agent? → **Table Storage**
2. Is this for admin configuration? → **SQL**
3. Is this for historical reporting? → **SQL**

**Example**: Get device list for dashboard
- Real-time data
- High frequency (every 30s)
- No complex aggregation
- **Decision**: Table Storage (`/api/devices/devices` endpoint)

### Scenario: Add new command type for agents

**Question**: How should commands be delivered?

**Answer**: Always use Queue Storage
1. Create command in SQL (audit record)
2. Enqueue to Azure Queue Storage (delivery)
3. Agent polls queue via `/api/agents/commands/receive`
4. Agent acknowledges on success
5. Agent reports completion to SQL (audit record)

**Pattern**: SQL for audit, Queue for delivery

### Scenario: Background job needs to process data

**Question**: Should it run continuously or be idle-aware?

**Decision Tree**:
1. Is it time-critical? → Run continuously
2. Can it wait until users are active? → Implement idle detection
3. Can it run off-peak hours? → Schedule for off-peak

**Example**: Device projection to Table Storage
- Not time-critical (10-30s lag acceptable)
- Can wait for user activity
- **Decision**: Implement idle detection, skip when idle

## Anti-Patterns to Avoid

### ❌ Anti-Pattern 1: Querying SQL from Real-Time Operations

```csharp
// ❌ BAD - Dashboard queries SQL every 30 seconds
var devices = await _db.Devices.ToListAsync();
```

**Fix**: Read from Table Storage
```csharp
// ✅ GOOD
var devices = await _deviceStore.GetAllForTenantAsync(_tenant.TenantId, ct);
```

### ❌ Anti-Pattern 2: Missing Tenant Filter

```csharp
// ❌ BAD - Exposes all tenant data
var device = await _db.Devices.FindAsync(id);
```

**Fix**: Always filter by tenant
```csharp
// ✅ GOOD
var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id && d.TenantId == _tenant.TenantId);
```

### ❌ Anti-Pattern 3: Logging Sensitive Data

```csharp
// ❌ BAD
_logger.LogInformation("API key: {ApiKey}", apiKey);
```

**Fix**: Never log secrets
```csharp
// ✅ GOOD
_logger.LogInformation("API key validated for agent {AgentId}", agentId);
```

### ❌ Anti-Pattern 4: Expensive Operations Without Batching

```csharp
// ❌ BAD - 100 individual operations
foreach (var device in devices)
{
    await _api.UpdateDeviceAsync(device);
}
```

**Fix**: Batch operations
```csharp
// ✅ GOOD - 1 batch operation
await _api.UpdateDevicesBatchAsync(devices);
```

### ❌ Anti-Pattern 5: No Idle Detection in Background Services

```csharp
// ❌ BAD - Queries SQL every 10 seconds forever
while (!stoppingToken.IsCancellationRequested)
{
    await ProcessDataAsync();
    await Task.Delay(10000, stoppingToken);
}
```

**Fix**: Check for idle state
```csharp
// ✅ GOOD
while (!stoppingToken.IsCancellationRequested)
{
    if (await _activityMonitor.IsSystemIdleAsync(stoppingToken))
    {
        await Task.Delay(30000, stoppingToken);
        continue;
    }
    
    await ProcessDataAsync();
    await Task.Delay(10000, stoppingToken);
}
```

## Validation and Testing

### Pre-Deployment Checklist

Use this for **every** change:

- [ ] Read relevant planning document(s)
- [ ] Multi-tenant isolation verified (tested with 2+ tenants)
- [ ] Security reviewed (authentication, authorization, input validation)
- [ ] Cost impact assessed (SQL queries minimized)
- [ ] Idle detection implemented (if background service)
- [ ] Table Storage used for real-time operations
- [ ] Error handling implemented
- [ ] Logging added (no sensitive data)
- [ ] Tests written and passing
- [ ] Performance tested under load
- [ ] Documentation updated
- [ ] Code review completed

### Testing Multi-Tenant Isolation

```csharp
[Fact]
public async Task GetDevice_FromDifferentTenant_Returns404()
{
    // Arrange
    var tenant1 = Guid.NewGuid();
    var tenant2 = Guid.NewGuid();
    
    var device = new Device { Id = Guid.NewGuid(), TenantId = tenant1 };
    _db.Devices.Add(device);
    await _db.SaveChangesAsync();
    
    // Act as tenant 2
    _mockTenantProvider.Setup(t => t.TenantId).Returns(tenant2);
    
    // Act
    var result = await _controller.GetDevice(device.Id);
    
    // Assert
    Assert.IsType<NotFoundResult>(result.Result);
}
```

## Maintenance and Updates

### When to Update These Documents

- New Azure service added to architecture
- New authentication mechanism implemented
- New cost optimization pattern discovered
- Security vulnerability found and fixed
- New development pattern established
- Common pitfall identified

### Document Ownership

All engineers can and should update these documents when:
- Discovering better patterns
- Finding gaps in guidance
- Identifying common mistakes
- Learning from production issues

## Quick Reference

### Storage Selection

| Data Type | Frequency | Storage | Rationale |
|-----------|-----------|---------|-----------|
| Device metadata | High read | SQL + Table | SQL for audit, Table for speed |
| Device status | Very high | Table only | High frequency, no audit needed |
| User accounts | Low | SQL only | ACID, low frequency |
| Agent commands | Medium | SQL + Queue | SQL for audit, Queue for delivery |
| Activity logs | Very high | Table only | High frequency, eventual consistency OK |

### Authentication Selection

| Client Type | Method | Token Lifetime | Refresh |
|-------------|--------|----------------|---------|
| Web users | Cookie | 8 hours | Sliding |
| Agents | JWT | 30 minutes | Auto (2-min buffer) |
| Legacy agents | API Key | N/A (static) | Manual rotation |

### Cost Optimization Priorities

1. **Eliminate SQL queries from real-time operations** (99% cost reduction)
2. **Implement idle detection** (80% reduction in off-hours)
3. **Use Queue Storage for commands** (95% cost reduction vs SQL polling)
4. **Batch operations** (40% reduction in HTTP overhead)
5. **Configure per-device ping frequency** (98% reduction for non-critical devices)

## Additional Resources

### External Documentation
- [Azure Table Storage Best Practices](https://docs.microsoft.com/en-us/azure/storage/tables/table-storage-design-guide)
- [Azure Queue Storage Best Practices](https://docs.microsoft.com/en-us/azure/storage/queues/storage-queues-introduction)
- [Blazor WebAssembly Performance](https://docs.microsoft.com/en-us/aspnet/core/blazor/performance)
- [Multi-Tenant SaaS Patterns](https://docs.microsoft.com/en-us/azure/architecture/guide/multitenant/overview)

### Repository Documentation
- [README.md](../../README.md) - Project overview
- [SQL Elimination Guide](../SQL_ELIMINATION_GUIDE.md) - Detailed SQL optimization
- [Idle Detection Guide](../IDLE_DETECTION.md) - Idle detection implementation
- [JWT Authentication](../JWT_AUTHENTICATION.md) - JWT implementation details
- [Queue Commands](../QUEUE_COMMANDS.md) - Queue-based command system
- [Deployment Guide](../../DEPLOYMENT.md) - Production deployment process

## Support and Questions

For questions about these planning documents or architecture decisions:
1. Review the specific planning document
2. Search for similar patterns in existing code
3. Check referenced documentation
4. Consult with team leads if still unclear

**Remember**: These documents are living guides. When you discover a better way, update them!
