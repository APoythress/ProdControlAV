using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProdControlAV.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAtemCommandFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AtemFunction",
                table: "Commands",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AtemInputId",
                table: "Commands",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AtemMacroId",
                table: "Commands",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AtemTransitionRate",
                table: "Commands",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AtemFunction",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "AtemInputId",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "AtemMacroId",
                table: "Commands");

            migrationBuilder.DropColumn(
                name: "AtemTransitionRate",
                table: "Commands");
        }
    }
}
