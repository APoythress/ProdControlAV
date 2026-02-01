# Quick Start Guide for Architecture Planning Documents

## 🎯 For Coding Agents: Start Here!

This is your first-stop guide when working on ProdControlAV. Use the decision tree below to find the right planning document.

## 🔍 What Are You Working On?

### 📊 Database/Data Changes
**Examples**: Adding a table, new column, changing data structure
- 📖 Read: [DATABASE-CHANGES.md](./DATABASE-CHANGES.md)
- 🔑 Key Questions:
  - SQL or Table Storage? → Decision matrix on page 1
  - Multi-tenant isolation? → Every query MUST filter by TenantId
  - Cost impact? → Table Storage is 10-100x cheaper

### 🏗️ Infrastructure/Services
**Examples**: Adding Azure service, background job, queue processing
- 📖 Read: [INFRASTRUCTURE-CHANGES.md](./INFRASTRUCTURE-CHANGES.md)
- 🔑 Key Questions:
  - New Azure service? → Justify cost vs existing services
  - Background service? → Implement idle detection
  - Queue vs Table vs SQL? → See service responsibilities table

### 🎨 User Interface
**Examples**: New page, component, form, dashboard widget
- 📖 Read: [UI-UX-CHANGES.md](./UI-UX-CHANGES.md)
- 🔑 Key Questions:
  - API calls? → Use Table Storage endpoints, not SQL
  - Real-time updates? → 30-second auto-refresh pattern
  - Mobile responsive? → Mobile-first grid layouts

### 🤖 Raspberry Pi Agent
**Examples**: Device monitoring, command execution, agent service
- 📖 Read: [AGENT-SERVICE-CHANGES.md](./AGENT-SERVICE-CHANGES.md)
- 🔑 Key Questions:
  - Command polling? → Use Queue Storage, not SQL
  - Device list? → Read from Table Storage
  - Authentication? → JWT tokens with auto-refresh

### 🔒 Security/Authentication
**Examples**: New endpoint, authentication, authorization, data access
- 📖 Read: [SECURITY-MULTITENANT.md](./SECURITY-MULTITENANT.md)
- 🔑 Key Questions:
  - Multi-tenant isolation? → ALWAYS filter by TenantId
  - Authentication method? → Cookie (users) or JWT (agents)
  - Input validation? → Always validate and sanitize

### 💰 Cost Impact
**Examples**: Evaluating storage options, optimization, monitoring
- 📖 Read: [COST-OPTIMIZATION.md](./COST-OPTIMIZATION.md)
- 🔑 Key Questions:
  - Storage choice? → Table Storage > Queue > SQL (by cost)
  - SQL queries? → Should be <100/hour during normal ops
  - Idle detection? → Background services MUST check idle state

## ⚡ Quick Decision Trees

### Should I Use SQL or Table Storage?

```
Is this data accessed frequently by dashboard/agents?
├─ YES → Is eventual consistency (10-30s lag) acceptable?
│  ├─ YES → ✅ Table Storage only
│  └─ NO → ⚠️ SQL + Table Storage (sync immediately)
└─ NO → Is this administrative/configuration data?
   ├─ YES → ✅ SQL only
   └─ NO → Is this audit/compliance data?
      ├─ YES → ✅ SQL only
      └─ NO → ✅ Default to Table Storage
```

### Should This Background Service Check Idle State?

```
Is this operation time-critical (seconds matter)?
├─ YES → ❌ No idle detection needed
└─ NO → Can this wait until users are active?
   ├─ YES → ✅ Implement idle detection
   └─ NO → Is this for reporting/analytics?
      ├─ YES → ✅ Schedule for off-peak hours
      └─ NO → ✅ Implement idle detection anyway
```

### What Authentication Should I Use?

```
Who is the client?
├─ Web browser (user) → 🍪 Cookie authentication
├─ Agent (Raspberry Pi) → 🎫 JWT authentication
└─ Script/automation → 🔑 API key (legacy, consider JWT)
```

## 🚨 Critical Rules (Never Break These!)

### 1. Multi-Tenant Isolation
```csharp
// ✅ ALWAYS DO THIS
var devices = await _db.Devices
    .Where(d => d.TenantId == _tenant.TenantId)
    .ToListAsync();

// ❌ NEVER DO THIS (security vulnerability!)
var devices = await _db.Devices.ToListAsync();
```

### 2. Cost Optimization
```csharp
// ✅ GOOD - Read from Table Storage
var devices = await _deviceStore.GetAllForTenantAsync(tenantId, ct);

// ❌ BAD - Dashboard querying SQL every 30 seconds
var devices = await _db.Devices.ToListAsync();
```

### 3. No Secrets in Logs
```csharp
// ✅ GOOD
_logger.LogInformation("User {Email} authenticated", email);

// ❌ BAD
_logger.LogInformation("User logged in with password {Password}", password);
```

### 4. Idle Detection
```csharp
// ✅ GOOD - Check idle state
if (await _activityMonitor.IsSystemIdleAsync(ct))
{
    _logger.LogDebug("System idle, skipping SQL operations");
    continue;
}
await ProcessSqlDataAsync(ct);

// ❌ BAD - Always running
await ProcessSqlDataAsync(ct); // Prevents SQL from idling
```

## 📋 Pre-Deployment Checklist

Use this for **EVERY** change:

```
[ ] Read relevant planning document(s)
[ ] Multi-tenant isolation verified (tested with 2+ tenants)
[ ] Security reviewed (auth, input validation, no secrets logged)
[ ] Cost impact assessed (SQL queries minimized, Table Storage used)
[ ] Error handling implemented
[ ] Tests written and passing
[ ] Documentation updated
```

## 📊 At-a-Glance Reference

### Storage Costs (per 1M operations/month)
- Table Storage: **$0.50**
- Queue Storage: **$0.40**
- SQL Database: **$5-30** (tier dependent)
→ **Savings: 90-98% using Table/Queue**

### Authentication Token Lifetimes
- Cookie (users): **8 hours** (sliding)
- JWT (agents): **30 minutes** (auto-refresh at 28min)
- API Key: **Static** (rotate manually)

### Recommended Polling Frequencies
- Dashboard auto-refresh: **30 seconds**
- Agent command poll: **10 seconds**
- Agent device list refresh: **5 minutes**
- Device status update: **5 seconds - 1 hour** (configurable per device)

## 🔗 Full Documentation

For comprehensive guidance, see:
- [Full Index with Examples](./README.md)
- [All Planning Documents](./README.md#document-structure)

## 💡 Pro Tips

1. **Start with README.md** - It has example workflows for common scenarios
2. **Use code examples** - All patterns include working code snippets
3. **Check anti-patterns** - Learn what NOT to do
4. **Follow checklists** - Each document has deployment checklists
5. **Cross-reference** - Security and cost apply to EVERYTHING

---

**Remember**: These documents save time and prevent costly mistakes. 5 minutes reading now = hours saved later!
