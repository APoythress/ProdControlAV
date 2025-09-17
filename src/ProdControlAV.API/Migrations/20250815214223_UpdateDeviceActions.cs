using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProdControlAV.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDeviceActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActionId",
                table: "DeviceActions",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "HttpMethod",
                table: "DeviceActions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActionId",
                table: "DeviceActions");

            migrationBuilder.DropColumn(
                name: "HttpMethod",
                table: "DeviceActions");
        }
    }
}
