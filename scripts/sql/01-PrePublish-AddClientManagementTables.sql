-- =============================================
-- Pre-Publish Migration Script
-- Client Management System - Database Schema
-- =============================================
-- Description: Creates new lookup tables and alters Tenants table
-- to support client management functionality
-- Execute BEFORE deploying application code changes
-- =============================================

SET NOCOUNT ON;
GO

-- =============================================
-- 1. Create TenantStatus lookup table
-- =============================================
PRINT 'Creating TenantStatus table...';
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TenantStatus' AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[TenantStatus] (
        [TenantStatusId] INT NOT NULL PRIMARY KEY,
        [TenantStatusText] NVARCHAR(25) NOT NULL
    );
    PRINT '✓ TenantStatus table created successfully';
END
ELSE
BEGIN
    PRINT '⚠ TenantStatus table already exists, skipping...';
END
GO

-- =============================================
-- 2. Create SubscriptionPlans lookup table
-- =============================================
PRINT 'Creating SubscriptionPlans table...';
GO

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SubscriptionPlans' AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[SubscriptionPlans] (
        [SubscriptionPlanId] INT NOT NULL PRIMARY KEY,
        [SubscriptionPlanText] NVARCHAR(25) NOT NULL
    );
    PRINT '✓ SubscriptionPlans table created successfully';
END
ELSE
BEGIN
    PRINT '⚠ SubscriptionPlans table already exists, skipping...';
END
GO

-- =============================================
-- 3. Seed TenantStatus with initial values
-- =============================================
PRINT 'Seeding TenantStatus table...';
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[TenantStatus])
BEGIN
    INSERT INTO [dbo].[TenantStatus] ([TenantStatusId], [TenantStatusText])
    VALUES 
        (1, 'Active'),
        (2, 'Pending'),
        (3, 'Terminated');
    PRINT '✓ TenantStatus seeded with 3 records';
END
ELSE
BEGIN
    PRINT '⚠ TenantStatus already has data, skipping seed...';
END
GO

-- =============================================
-- 4. Seed SubscriptionPlans with initial values
-- =============================================
PRINT 'Seeding SubscriptionPlans table...';
GO

IF NOT EXISTS (SELECT 1 FROM [dbo].[SubscriptionPlans])
BEGIN
    INSERT INTO [dbo].[SubscriptionPlans] ([SubscriptionPlanId], [SubscriptionPlanText])
    VALUES 
        (0, 'N/A'),
        (1, 'Base'),
        (2, 'Pro');
    PRINT '✓ SubscriptionPlans seeded with 3 records';
END
ELSE
BEGIN
    PRINT '⚠ SubscriptionPlans already has data, skipping seed...';
END
GO

-- =============================================
-- 5. Alter Tenants table to add foreign keys
-- =============================================
PRINT 'Altering Tenants table...';
GO

-- Add TenantStatusId column if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'TenantStatusId')
BEGIN
    ALTER TABLE [dbo].[Tenants] 
    ADD [TenantStatusId] INT NULL;
    PRINT '✓ Added TenantStatusId column to Tenants table';
END
ELSE
BEGIN
    PRINT '⚠ TenantStatusId column already exists';
END
GO

-- Add SubscriptionPlanId column if it doesn't exist
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'SubscriptionPlanId')
BEGIN
    ALTER TABLE [dbo].[Tenants] 
    ADD [SubscriptionPlanId] INT NULL;
    PRINT '✓ Added SubscriptionPlanId column to Tenants table';
END
ELSE
BEGIN
    PRINT '⚠ SubscriptionPlanId column already exists';
END
GO

-- =============================================
-- 6. Create foreign key constraints
-- =============================================
PRINT 'Creating foreign key constraints...';
GO

-- FK for TenantStatusId
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Tenants_TenantStatus_TenantStatusId')
BEGIN
    ALTER TABLE [dbo].[Tenants]
    ADD CONSTRAINT [FK_Tenants_TenantStatus_TenantStatusId] 
    FOREIGN KEY ([TenantStatusId]) 
    REFERENCES [dbo].[TenantStatus] ([TenantStatusId])
    ON DELETE SET NULL;
    PRINT '✓ Foreign key FK_Tenants_TenantStatus_TenantStatusId created';
END
ELSE
BEGIN
    PRINT '⚠ Foreign key FK_Tenants_TenantStatus_TenantStatusId already exists';
END
GO

-- FK for SubscriptionPlanId
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Tenants_SubscriptionPlans_SubscriptionPlanId')
BEGIN
    ALTER TABLE [dbo].[Tenants]
    ADD CONSTRAINT [FK_Tenants_SubscriptionPlans_SubscriptionPlanId] 
    FOREIGN KEY ([SubscriptionPlanId]) 
    REFERENCES [dbo].[SubscriptionPlans] ([SubscriptionPlanId])
    ON DELETE SET NULL;
    PRINT '✓ Foreign key FK_Tenants_SubscriptionPlans_SubscriptionPlanId created';
END
ELSE
BEGIN
    PRINT '⚠ Foreign key FK_Tenants_SubscriptionPlans_SubscriptionPlanId already exists';
END
GO

-- =============================================
-- 7. Create indexes for performance
-- =============================================
PRINT 'Creating indexes...';
GO

-- Index on Tenants.TenantStatusId for faster joins
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Tenants_TenantStatusId' AND object_id = OBJECT_ID('dbo.Tenants'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Tenants_TenantStatusId] 
    ON [dbo].[Tenants] ([TenantStatusId]);
    PRINT '✓ Index IX_Tenants_TenantStatusId created';
END
ELSE
BEGIN
    PRINT '⚠ Index IX_Tenants_TenantStatusId already exists';
END
GO

-- Index on Tenants.SubscriptionPlanId for faster joins
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Tenants_SubscriptionPlanId' AND object_id = OBJECT_ID('dbo.Tenants'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_Tenants_SubscriptionPlanId] 
    ON [dbo].[Tenants] ([SubscriptionPlanId]);
    PRINT '✓ Index IX_Tenants_SubscriptionPlanId created';
END
ELSE
BEGIN
    PRINT '⚠ Index IX_Tenants_SubscriptionPlanId already exists';
END
GO

-- =============================================
-- 8. Set default values for existing tenants (optional)
-- =============================================
PRINT 'Setting default values for existing tenants...';
GO

-- Set all existing tenants to Active status (optional)
UPDATE [dbo].[Tenants]
SET [TenantStatusId] = 1  -- Active
WHERE [TenantStatusId] IS NULL;

DECLARE @UpdatedCount INT = @@ROWCOUNT;
PRINT '✓ Updated ' + CAST(@UpdatedCount AS VARCHAR(10)) + ' existing tenant(s) to Active status';
GO

-- =============================================
-- Migration Complete
-- =============================================
PRINT '';
PRINT '========================================';
PRINT 'Migration completed successfully!';
PRINT '========================================';
PRINT 'Tables created:';
PRINT '  - TenantStatus (3 records)';
PRINT '  - SubscriptionPlans (3 records)';
PRINT 'Columns added to Tenants:';
PRINT '  - TenantStatusId (nullable INT)';
PRINT '  - SubscriptionPlanId (nullable INT)';
PRINT 'Foreign keys created with ON DELETE SET NULL';
PRINT 'Indexes created for performance optimization';
PRINT '';
PRINT 'You can now deploy the application code.';
PRINT '========================================';
GO
