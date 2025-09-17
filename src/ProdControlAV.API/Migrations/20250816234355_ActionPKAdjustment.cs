using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProdControlAV.API.Migrations
{
    /// <inheritdoc />
    public partial class ActionPKAdjustment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DeviceActions",
                table: "DeviceActions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeviceActions",
                table: "DeviceActions",
                column: "ActionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_DeviceActions",
                table: "DeviceActions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DeviceActions",
                table: "DeviceActions",
                columns: new[] { "DeviceId", "TenantId" });
        }
    }
}
