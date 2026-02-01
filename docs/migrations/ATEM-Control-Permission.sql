-- Migration Script for ATEM Control Feature
-- This script adds the UserPermissions table and seeds the ATEM-Control permission

-- Create UserPermissions table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserPermissions')
BEGIN
    CREATE TABLE [dbo].[UserPermissions] (
        [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        [UserId] UNIQUEIDENTIFIER NOT NULL,
        [Permission] NVARCHAR(100) NOT NULL,
        [CreatedAt] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        
        -- Foreign key constraint
        CONSTRAINT [FK_UserPermissions_Users_UserId] 
            FOREIGN KEY ([UserId]) REFERENCES [Users]([UserId]) ON DELETE CASCADE,
        
        -- Unique constraint on UserId and Permission combination
        CONSTRAINT [UQ_UserPermissions_UserId_Permission] 
            UNIQUE ([UserId], [Permission])
    );

    -- Create index on UserId for efficient permission lookups
    CREATE INDEX [IX_UserPermissions_UserId] 
        ON [dbo].[UserPermissions]([UserId]);
    
    PRINT 'UserPermissions table created successfully';
END
ELSE
BEGIN
    PRINT 'UserPermissions table already exists';
END
GO

-- Grant ATEM-Control permission to existing users (optional - adjust as needed)
-- Example: Grant to all users with Admin or DevAdmin role
/*
INSERT INTO [dbo].[UserPermissions] ([Id], [UserId], [Permission], [CreatedAt])
SELECT 
    NEWID(),
    ut.[UserId],
    'ATEM-Control',
    SYSDATETIMEOFFSET()
FROM [dbo].[UserTenants] ut
WHERE ut.[Role] IN ('Admin', 'DevAdmin')
AND NOT EXISTS (
    SELECT 1 FROM [dbo].[UserPermissions] up 
    WHERE up.[UserId] = ut.[UserId] 
    AND up.[Permission] = 'ATEM-Control'
);
*/

-- Or grant to specific users by email
/*
INSERT INTO [dbo].[UserPermissions] ([Id], [UserId], [Permission], [CreatedAt])
SELECT 
    NEWID(),
    u.[UserId],
    'ATEM-Control',
    SYSDATETIMEOFFSET()
FROM [dbo].[Users] u
WHERE u.[Email] IN ('admin@example.com', 'user@example.com')
AND NOT EXISTS (
    SELECT 1 FROM [dbo].[UserPermissions] up 
    WHERE up.[UserId] = u.[UserId] 
    AND up.[Permission] = 'ATEM-Control'
);
*/

PRINT 'ATEM Control permission migration completed';
GO
