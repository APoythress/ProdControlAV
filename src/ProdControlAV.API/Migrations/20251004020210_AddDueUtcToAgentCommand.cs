using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProdControlAV.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDueUtcToAgentCommand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserTenants_Tenants_TenantId",
                table: "UserTenants");

            // Add Location column only if it does not already exist (protect against manual or out-of-band schema changes)
            migrationBuilder.Sql(@"IF NOT EXISTS(SELECT * FROM sys.columns WHERE Name = N'Location' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices ADD Location nvarchar(max) NULL;
END");

            migrationBuilder.AddColumn<DateTime>(
                name: "DueUtc",
                table: "AgentCommands",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserTenants_Tenants_TenantId",
                table: "UserTenants",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserTenants_Tenants_TenantId",
                table: "UserTenants");

            // Drop Location only if it exists to keep Down idempotent and safe
            migrationBuilder.Sql(@"IF EXISTS(SELECT * FROM sys.columns WHERE Name = N'Location' AND Object_ID = Object_ID(N'dbo.Devices'))
BEGIN
    ALTER TABLE dbo.Devices DROP COLUMN Location;
END");

            migrationBuilder.DropColumn(
                name: "DueUtc",
                table: "AgentCommands");

            migrationBuilder.AddForeignKey(
                name: "FK_UserTenants_Tenants_TenantId",
                table: "UserTenants",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
