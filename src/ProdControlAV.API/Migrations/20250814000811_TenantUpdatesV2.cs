using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProdControlAV.API.Migrations
{
    /// <inheritdoc />
    public partial class TenantUpdatesV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserTenants_Tenants_TenantId",
                table: "UserTenants");

            migrationBuilder.DropForeignKey(
                name: "FK_UserTenants_Users_AppUserId",
                table: "UserTenants");

            migrationBuilder.DropIndex(
                name: "IX_UserTenants_AppUserId",
                table: "UserTenants");

            migrationBuilder.DropIndex(
                name: "IX_UserTenants_TenantId",
                table: "UserTenants");

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "UserTenants");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "Users",
                newName: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "Users",
                newName: "Id");

            migrationBuilder.AddColumn<Guid>(
                name: "AppUserId",
                table: "UserTenants",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTenants_AppUserId",
                table: "UserTenants",
                column: "AppUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserTenants_TenantId",
                table: "UserTenants",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserTenants_Tenants_TenantId",
                table: "UserTenants",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserTenants_Users_AppUserId",
                table: "UserTenants",
                column: "AppUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }
    }
}
