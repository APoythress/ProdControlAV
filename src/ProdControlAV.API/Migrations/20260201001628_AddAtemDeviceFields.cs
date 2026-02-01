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
            migrationBuilder.AddColumn<bool>(
                name: "AtemEnabled",
                table: "Devices",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AtemTransitionDefaultRate",
                table: "Devices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AtemTransitionDefaultType",
                table: "Devices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RecordingStatus",
                table: "Devices",
                type: "bit",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CommandTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "RequireDeviceOnline",
                table: "Commands",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "MonitorRecordingStatus",
                table: "Commands",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "StatusEndpoint",
                table: "Commands",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StatusPollingIntervalSeconds",
                table: "Commands",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AtemEnabled",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "AtemTransitionDefaultRate",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "AtemTransitionDefaultType",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "RecordingStatus",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "MonitorRecordingStatus",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "StatusEndpoint",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "StatusPollingIntervalSeconds",
                table: "Commands");

            migrationBuilder.AlterColumn<string>(
                name: "Description",
                table: "CommandTemplates",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<bool>(
                name: "RequireDeviceOnline",
                table: "Commands",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");
        }
    }
}
