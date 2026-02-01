# Client Management SQL Scripts

This directory contains SQL migration scripts for the Client Management feature.

## Scripts Overview

### 1. `01-PrePublish-AddClientManagementTables.sql`
**Execute BEFORE deploying application code**

This script:
- Creates `TenantStatus` lookup table with 3 default values (Active, Pending, Terminated)
- Creates `SubscriptionPlans` lookup table with 3 default values (N/A, Base, Pro)
- Alters `Tenants` table to add nullable foreign key columns
- Creates foreign key constraints with `ON DELETE SET NULL`
- Creates performance indexes
- Sets existing tenants to "Active" status by default
- Includes idempotency checks (safe to run multiple times)

### 2. `02-PostPublish-VerifyClientManagement.sql`
**Execute AFTER deploying application code**

This script:
- Verifies all tables were created successfully
- Checks foreign key constraints exist
- Validates indexes were created
- Shows tenant statistics and distribution
- Tests the query used by the API endpoint
- Provides diagnostic information

## Execution Instructions

### For Azure SQL Database

#### Option A: Using Azure Portal
1. Navigate to your Azure SQL Database
2. Click "Query editor" in the left menu
3. Authenticate with your credentials
4. Copy and paste the pre-publish script
5. Click "Run"
6. Review the output for any errors
7. Deploy your application code
8. Return to Query editor and run the post-publish script

#### Option B: Using Azure Data Studio
1. Connect to your Azure SQL Database
2. Open `01-PrePublish-AddClientManagementTables.sql`
3. Click "Run" or press F5
4. Review the Messages tab for output
5. Deploy your application code
6. Open `02-PostPublish-VerifyClientManagement.sql`
7. Click "Run" or press F5

#### Option C: Using SQL Server Management Studio (SSMS)
1. Connect to your Azure SQL Database
2. Open `01-PrePublish-AddClientManagementTables.sql`
3. Press F5 to execute
4. Check Messages tab for output
5. Deploy your application code
6. Open `02-PostPublish-VerifyClientManagement.sql`
7. Press F5 to execute

#### Option D: Using Azure CLI
```bash
# Pre-publish script
az sql db query \
  --server your-server-name \
  --database your-database-name \
  --auth-type SqlPassword \
  --username your-username \
  --password "your-password" \
  --file 01-PrePublish-AddClientManagementTables.sql

# Deploy application
# ... your deployment process ...

# Post-publish script
az sql db query \
  --server your-server-name \
  --database your-database-name \
  --auth-type SqlPassword \
  --username your-username \
  --password "your-password" \
  --file 02-PostPublish-VerifyClientManagement.sql
```

### For Local SQL Server

```bash
# Using sqlcmd
sqlcmd -S localhost -d ProdControlAV -i 01-PrePublish-AddClientManagementTables.sql

# Deploy application
# ... your deployment process ...

# Verify
sqlcmd -S localhost -d ProdControlAV -i 02-PostPublish-VerifyClientManagement.sql
```

## Database Schema Changes

### New Tables

#### TenantStatus
| Column | Type | Constraints |
|--------|------|-------------|
| TenantStatusId | INT | PRIMARY KEY |
| TenantStatusText | NVARCHAR(25) | NOT NULL |

**Initial Data:**
- 1 = Active
- 2 = Pending
- 3 = Terminated

#### SubscriptionPlans
| Column | Type | Constraints |
|--------|------|-------------|
| SubscriptionPlanId | INT | PRIMARY KEY |
| SubscriptionPlanText | NVARCHAR(25) | NOT NULL |

**Initial Data:**
- 0 = N/A
- 1 = Base
- 2 = Pro

### Modified Tables

#### Tenants (Added Columns)
| Column | Type | Constraints |
|--------|------|-------------|
| TenantStatusId | INT | NULL, FK to TenantStatus |
| SubscriptionPlanId | INT | NULL, FK to SubscriptionPlans |

**Foreign Keys:**
- `FK_Tenants_TenantStatus_TenantStatusId` with `ON DELETE SET NULL`
- `FK_Tenants_SubscriptionPlans_SubscriptionPlanId` with `ON DELETE SET NULL`

**Indexes:**
- `IX_Tenants_TenantStatusId` (nonclustered)
- `IX_Tenants_SubscriptionPlanId` (nonclustered)

## Safety Features

Both scripts include:
- ✅ Idempotency checks (safe to run multiple times)
- ✅ Verbose output with status messages
- ✅ Error handling
- ✅ Transaction safety (implicit per statement)
- ✅ No data loss (nullable columns, SET NULL on delete)

## Rollback (If Needed)

If you need to rollback these changes:

```sql
-- Remove foreign key constraints
ALTER TABLE [dbo].[Tenants] DROP CONSTRAINT IF EXISTS [FK_Tenants_TenantStatus_TenantStatusId];
ALTER TABLE [dbo].[Tenants] DROP CONSTRAINT IF EXISTS [FK_Tenants_SubscriptionPlans_SubscriptionPlanId];

-- Remove indexes
DROP INDEX IF EXISTS [IX_Tenants_TenantStatusId] ON [dbo].[Tenants];
DROP INDEX IF EXISTS [IX_Tenants_SubscriptionPlanId] ON [dbo].[Tenants];

-- Remove columns from Tenants
ALTER TABLE [dbo].[Tenants] DROP COLUMN IF EXISTS [TenantStatusId];
ALTER TABLE [dbo].[Tenants] DROP COLUMN IF EXISTS [SubscriptionPlanId];

-- Drop lookup tables
DROP TABLE IF EXISTS [dbo].[TenantStatus];
DROP TABLE IF EXISTS [dbo].[SubscriptionPlans];
```

## Troubleshooting

### Error: "Cannot create foreign key constraint"
- Ensure lookup tables are created before adding foreign keys
- Verify lookup tables have the correct primary keys
- Check that column data types match exactly

### Error: "Column already exists"
- This is normal if re-running the script
- The script checks for existence before creating
- Review output to confirm columns exist

### Warning: "Performance may be affected"
- Indexes might not have been created
- Re-run the index creation portion
- Verify using the post-publish script

## Support

For issues or questions:
1. Review the verification script output
2. Check Azure SQL Database query logs
3. Verify application logs for database errors
4. Contact your database administrator

## Related Files

- Models: `src/ProdControlAV.Core/Models/Tenant.cs`
- Models: `src/ProdControlAV.Core/Models/TenantStatus.cs`
- Models: `src/ProdControlAV.Core/Models/SubscriptionPlan.cs`
- API: `src/ProdControlAV.API/Controllers/TenantsController.cs`
- UI: `src/ProdControlAV.WebApp/Pages/ClientManager.razor`
