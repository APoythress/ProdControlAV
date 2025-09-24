using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProdControlAV.API.Migrations
{
    /// <inheritdoc />
    public partial class FixCascadeDeleteConflicts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserTenants_Tenants_TenantId",
                table: "UserTenants");

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
