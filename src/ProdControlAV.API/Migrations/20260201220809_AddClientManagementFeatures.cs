using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProdControlAV.API.Migrations
{
    /// <inheritdoc />
    public partial class AddClientManagementFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Tenants",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tenants",
                type: "nvarchar(250)",
                maxLength: 250,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            // migrationBuilder.AddColumn<int>(
            //     name: "SubscriptionPlanId",
            //     table: "Tenants",
            //     type: "int",
            //     nullable: true);

            // migrationBuilder.AddColumn<int>(
            //     name: "TenantStatusId",
            //     table: "Tenants",
            //     type: "int",
            //     nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubLocation",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Agents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<string>(
                name: "LocationName",
                table: "Agents",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NoteText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientNotes", x => x.Id);
                });

            // migrationBuilder.CreateTable(
            //     name: "SubscriptionPlans",
            //     columns: table => new
            //     {
            //         SubscriptionPlanId = table.Column<int>(type: "int", nullable: false)
            //             .Annotation("SqlServer:Identity", "1, 1"),
            //         SubscriptionPlanText = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false)
            //     },
            //     constraints: table =>
            //     {
            //         table.PrimaryKey("PK_SubscriptionPlans", x => x.SubscriptionPlanId);
            //     });

            // migrationBuilder.CreateTable(
            //     name: "TenantStatus",
            //     columns: table => new
            //     {
            //         TenantStatusId = table.Column<int>(type: "int", nullable: false)
            //             .Annotation("SqlServer:Identity", "1, 1"),
            //         TenantStatusText = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false)
            //     },
            //     constraints: table =>
            //     {
            //         table.PrimaryKey("PK_TenantStatus", x => x.TenantStatusId);
            //     });

            // migrationBuilder.CreateIndex(
            //     name: "IX_Tenants_SubscriptionPlanId",
            //     table: "Tenants",
            //     column: "SubscriptionPlanId");
            //
            // migrationBuilder.CreateIndex(
            //     name: "IX_Tenants_TenantStatusId",
            //     table: "Tenants",
            //     column: "TenantStatusId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_TenantId",
                table: "Agents",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientNotes_TenantId_CreatedUtc",
                table: "ClientNotes",
                columns: new[] { "TenantId", "CreatedUtc" });

            // migrationBuilder.AddForeignKey(
            //     name: "FK_Tenants_SubscriptionPlans_SubscriptionPlanId",
            //     table: "Tenants",
            //     column: "SubscriptionPlanId",
            //     principalTable: "SubscriptionPlans",
            //     principalColumn: "SubscriptionPlanId",
            //     onDelete: ReferentialAction.SetNull);
            //
            // migrationBuilder.AddForeignKey(
            //     name: "FK_Tenants_TenantStatus_TenantStatusId",
            //     table: "Tenants",
            //     column: "TenantStatusId",
            //     principalTable: "TenantStatus",
            //     principalColumn: "TenantStatusId",
            //     onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // migrationBuilder.DropForeignKey(
            //     name: "FK_Tenants_SubscriptionPlans_SubscriptionPlanId",
            //     table: "Tenants");
            //
            // migrationBuilder.DropForeignKey(
            //     name: "FK_Tenants_TenantStatus_TenantStatusId",
            //     table: "Tenants");

            migrationBuilder.DropTable(
                name: "ClientNotes");

            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropTable(
                name: "TenantStatus");

            // migrationBuilder.DropIndex(
            //     name: "IX_Tenants_SubscriptionPlanId",
            //     table: "Tenants");
            //
            // migrationBuilder.DropIndex(
            //     name: "IX_Tenants_TenantStatusId",
            //     table: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Agents_TenantId",
                table: "Agents");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TenantStatusId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubLocation",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LocationName",
                table: "Agents");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Tenants",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(250)",
                oldMaxLength: 250);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Tenants",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(250)",
                oldMaxLength: 250);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Agents",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);
        }
    }
}
