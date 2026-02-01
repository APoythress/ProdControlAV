-- =============================================
-- Post-Publish Verification Script
-- Client Management System - Database Schema
-- =============================================
-- Description: Verifies that all database changes were
-- applied correctly and provides diagnostic information
-- Execute AFTER deploying application code changes
-- =============================================

SET NOCOUNT ON;
GO

PRINT '========================================';
PRINT 'Client Management Migration Verification';
PRINT '========================================';
PRINT '';

-- =============================================
-- 1. Verify TenantStatus table
-- =============================================
PRINT '1. Checking TenantStatus table...';
GO

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'TenantStatus' AND type = 'U')
BEGIN
    DECLARE @TenantStatusCount INT;
    SELECT @TenantStatusCount = COUNT(*) FROM [dbo].[TenantStatus];
    PRINT '   ✓ TenantStatus table exists';
    PRINT '   ✓ Contains ' + CAST(@TenantStatusCount AS VARCHAR(10)) + ' record(s)';
    
    -- Show data
    PRINT '   Data:';
    SELECT '     ' + CAST(TenantStatusId AS VARCHAR(5)) + ' - ' + TenantStatusText AS [Status Values]
    FROM [dbo].[TenantStatus]
    ORDER BY TenantStatusId;
END
ELSE
BEGIN
    PRINT '   ✗ ERROR: TenantStatus table not found!';
END
PRINT '';
GO

-- =============================================
-- 2. Verify SubscriptionPlans table
-- =============================================
PRINT '2. Checking SubscriptionPlans table...';
GO

IF EXISTS (SELECT * FROM sys.tables WHERE name = 'SubscriptionPlans' AND type = 'U')
BEGIN
    DECLARE @SubscriptionPlansCount INT;
    SELECT @SubscriptionPlansCount = COUNT(*) FROM [dbo].[SubscriptionPlans];
    PRINT '   ✓ SubscriptionPlans table exists';
    PRINT '   ✓ Contains ' + CAST(@SubscriptionPlansCount AS VARCHAR(10)) + ' record(s)';
    
    -- Show data
    PRINT '   Data:';
    SELECT '     ' + CAST(SubscriptionPlanId AS VARCHAR(5)) + ' - ' + SubscriptionPlanText AS [Plan Values]
    FROM [dbo].[SubscriptionPlans]
    ORDER BY SubscriptionPlanId;
END
ELSE
BEGIN
    PRINT '   ✗ ERROR: SubscriptionPlans table not found!';
END
PRINT '';
GO

-- =============================================
-- 3. Verify Tenants table alterations
-- =============================================
PRINT '3. Checking Tenants table alterations...';
GO

-- Check TenantStatusId column
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'TenantStatusId')
BEGIN
    PRINT '   ✓ TenantStatusId column exists';
END
ELSE
BEGIN
    PRINT '   ✗ ERROR: TenantStatusId column not found!';
END

-- Check SubscriptionPlanId column
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Tenants') AND name = 'SubscriptionPlanId')
BEGIN
    PRINT '   ✓ SubscriptionPlanId column exists';
END
ELSE
BEGIN
    PRINT '   ✗ ERROR: SubscriptionPlanId column not found!';
END
PRINT '';
GO

-- =============================================
-- 4. Verify foreign key constraints
-- =============================================
PRINT '4. Checking foreign key constraints...';
GO

IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Tenants_TenantStatus_TenantStatusId')
BEGIN
    PRINT '   ✓ FK_Tenants_TenantStatus_TenantStatusId exists';
END
ELSE
BEGIN
    PRINT '   ✗ ERROR: FK_Tenants_TenantStatus_TenantStatusId not found!';
END

IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Tenants_SubscriptionPlans_SubscriptionPlanId')
BEGIN
    PRINT '   ✓ FK_Tenants_SubscriptionPlans_SubscriptionPlanId exists';
END
ELSE
BEGIN
    PRINT '   ✗ ERROR: FK_Tenants_SubscriptionPlans_SubscriptionPlanId not found!';
END
PRINT '';
GO

-- =============================================
-- 5. Verify indexes
-- =============================================
PRINT '5. Checking indexes...';
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Tenants_TenantStatusId' AND object_id = OBJECT_ID('dbo.Tenants'))
BEGIN
    PRINT '   ✓ IX_Tenants_TenantStatusId exists';
END
ELSE
BEGIN
    PRINT '   ⚠ WARNING: IX_Tenants_TenantStatusId not found (performance may be affected)';
END

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Tenants_SubscriptionPlanId' AND object_id = OBJECT_ID('dbo.Tenants'))
BEGIN
    PRINT '   ✓ IX_Tenants_SubscriptionPlanId exists';
END
ELSE
BEGIN
    PRINT '   ⚠ WARNING: IX_Tenants_SubscriptionPlanId not found (performance may be affected)';
END
PRINT '';
GO

-- =============================================
-- 6. Show tenant statistics
-- =============================================
PRINT '6. Tenant statistics...';
GO

DECLARE @TotalTenants INT;
DECLARE @TenantsWithStatus INT;
DECLARE @TenantsWithSubscription INT;

SELECT @TotalTenants = COUNT(*) FROM [dbo].[Tenants];
SELECT @TenantsWithStatus = COUNT(*) FROM [dbo].[Tenants] WHERE TenantStatusId IS NOT NULL;
SELECT @TenantsWithSubscription = COUNT(*) FROM [dbo].[Tenants] WHERE SubscriptionPlanId IS NOT NULL;

PRINT '   Total tenants: ' + CAST(@TotalTenants AS VARCHAR(10));
PRINT '   Tenants with status: ' + CAST(@TenantsWithStatus AS VARCHAR(10));
PRINT '   Tenants with subscription: ' + CAST(@TenantsWithSubscription AS VARCHAR(10));
PRINT '';

-- Show breakdown by status
PRINT '   Tenant breakdown by status:';
SELECT 
    ISNULL(ts.TenantStatusText, 'Not Set') AS [Status],
    COUNT(*) AS [Count]
FROM [dbo].[Tenants] t
LEFT JOIN [dbo].[TenantStatus] ts ON t.TenantStatusId = ts.TenantStatusId
GROUP BY ts.TenantStatusText
ORDER BY COUNT(*) DESC;
PRINT '';
GO

-- =============================================
-- 7. Test query (same as API endpoint)
-- =============================================
PRINT '7. Testing client management query...';
GO

SELECT TOP 5
    t.Id AS TenantId,
    t.Name,
    t.Slug,
    t.CreatedUtc,
    ISNULL(ts.TenantStatusText, 'Active') AS [Status],
    ISNULL(sp.SubscriptionPlanText, 'N/A') AS Subscription,
    (SELECT COUNT(*) FROM [dbo].[Devices] d WHERE d.TenantId = t.Id) AS DeviceCount
FROM [dbo].[Tenants] t
LEFT JOIN [dbo].[TenantStatus] ts ON t.TenantStatusId = ts.TenantStatusId
LEFT JOIN [dbo].[SubscriptionPlans] sp ON t.SubscriptionPlanId = sp.SubscriptionPlanId
ORDER BY t.Name;

PRINT '';
PRINT '✓ Query executed successfully (showing first 5 tenants)';
PRINT '';
GO

-- =============================================
-- Verification Complete
-- =============================================
PRINT '========================================';
PRINT 'Verification completed!';
PRINT '========================================';
PRINT '';
PRINT 'Next steps:';
PRINT '1. Verify the application UI is accessible';
PRINT '2. Navigate to /clientmanager page';
PRINT '3. Verify client data displays correctly';
PRINT '4. Test sorting and filtering if applicable';
PRINT '';
PRINT 'If you see any errors above, review the';
PRINT 'pre-publish script and re-run if necessary.';
PRINT '========================================';
GO
