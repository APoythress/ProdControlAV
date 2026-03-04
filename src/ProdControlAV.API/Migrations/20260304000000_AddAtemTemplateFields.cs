using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProdControlAV.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAtemTemplateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add AtemChannel to Commands (used by SetAux / GetAuxSource)
            migrationBuilder.AddColumn<int>(
                name: "AtemChannel",
                table: "Commands",
                type: "int",
                nullable: true);

            // Add ATEM-specific metadata columns to CommandTemplates
            migrationBuilder.AddColumn<string>(
                name: "AtemFunction",
                table: "CommandTemplates",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AtemInputId",
                table: "CommandTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AtemTransitionRate",
                table: "CommandTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AtemMacroId",
                table: "CommandTemplates",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AtemChannel",
                table: "CommandTemplates",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AtemChannel",       table: "Commands");
            migrationBuilder.DropColumn(name: "AtemFunction",      table: "CommandTemplates");
            migrationBuilder.DropColumn(name: "AtemInputId",       table: "CommandTemplates");
            migrationBuilder.DropColumn(name: "AtemTransitionRate", table: "CommandTemplates");
            migrationBuilder.DropColumn(name: "AtemMacroId",       table: "CommandTemplates");
            migrationBuilder.DropColumn(name: "AtemChannel",       table: "CommandTemplates");
        }
    }
}
