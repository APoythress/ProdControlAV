using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProdControlAV.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAtemDeviceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add columns if they do not already exist (idempotent)
            migrationBuilder.Sql(@"IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'AtemEnabled' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices ADD AtemEnabled bit NULL;
END");

            migrationBuilder.Sql(@"IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'AtemTransitionDefaultRate' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices ADD AtemTransitionDefaultRate int NULL;
END");

            migrationBuilder.Sql(@"IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'AtemTransitionDefaultType' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices ADD AtemTransitionDefaultType nvarchar(max) NULL;
END");

            migrationBuilder.Sql(@"IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'RecordingStatus' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices ADD RecordingStatus bit NULL;
END");

            // Alter column Description on CommandTemplates - only if it's currently nullable
            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'Description' AND Object_ID = Object_ID(N'dbo.CommandTemplates'))
AND EXISTS(SELECT 1 FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id WHERE c.name = N'Description' AND t.name = N'CommandTemplates' AND c.is_nullable = 1)
BEGIN
    ALTER TABLE dbo.CommandTemplates ALTER COLUMN Description nvarchar(500) NOT NULL;
    -- Set default empty string for existing NULLs
    UPDATE dbo.CommandTemplates SET Description = '' WHERE Description IS NULL;
END");

            // Change RequireDeviceOnline default - safest is to update existing nulls and remove default if necessary
            migrationBuilder.Sql(@"-- Ensure no NULLs exist for RequireDeviceOnline before enforcing non-null
IF EXISTS(SELECT 1 FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id WHERE c.name = N'RequireDeviceOnline' AND t.name = N'Commands')
BEGIN
    UPDATE dbo.Commands SET RequireDeviceOnline = 0 WHERE RequireDeviceOnline IS NULL;
END");

            migrationBuilder.Sql(@"IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'MonitorRecordingStatus' AND Object_ID = Object_ID(N'dbo.Commands'))
BEGIN
    ALTER TABLE dbo.Commands ADD MonitorRecordingStatus bit NOT NULL CONSTRAINT DF_Commands_MonitorRecordingStatus DEFAULT(0);
END");

            migrationBuilder.Sql(@"IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'StatusEndpoint' AND Object_ID = Object_ID(N'dbo.Commands'))
BEGIN
    ALTER TABLE dbo.Commands ADD StatusEndpoint nvarchar(500) NULL;
END");

            migrationBuilder.Sql(@"IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'StatusPollingIntervalSeconds' AND Object_ID = Object_ID(N'dbo.Commands'))
BEGIN
    ALTER TABLE dbo.Commands ADD StatusPollingIntervalSeconds int NOT NULL CONSTRAINT DF_Commands_StatusPollingIntervalSeconds DEFAULT(0);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop columns if they exist
            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'AtemEnabled' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices DROP COLUMN AtemEnabled;
END");

            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'AtemTransitionDefaultRate' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices DROP COLUMN AtemTransitionDefaultRate;
END");

            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'AtemTransitionDefaultType' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices DROP COLUMN AtemTransitionDefaultType;
END");

            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'RecordingStatus' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices DROP COLUMN RecordingStatus;
END");

            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'MonitorRecordingStatus' AND Object_ID = Object_ID(N'dbo.Commands'))
BEGIN
    ALTER TABLE dbo.Commands DROP COLUMN MonitorRecordingStatus;
END");

            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'StatusEndpoint' AND Object_ID = Object_ID(N'dbo.Commands'))
BEGIN
    ALTER TABLE dbo.Commands DROP COLUMN StatusEndpoint;
END");

            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'StatusPollingIntervalSeconds' AND Object_ID = Object_ID(N'dbo.Commands'))
BEGIN
    ALTER TABLE dbo.Commands DROP COLUMN StatusPollingIntervalSeconds;
END");

            // Revert Description nullable if column exists
            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id WHERE c.name = N'Description' AND t.name = N'CommandTemplates')
BEGIN
    ALTER TABLE dbo.CommandTemplates ALTER COLUMN Description nvarchar(500) NULL;
END");

            // Reapply default on RequireDeviceOnline if necessary
            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns c JOIN sys.tables t ON c.object_id = t.object_id WHERE c.name = N'RequireDeviceOnline' AND t.name = N'Commands')
BEGIN
    -- No-op: database may already have the default set; further adjustments can be done manually
END");
        }
    }
}
