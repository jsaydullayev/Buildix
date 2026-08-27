using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastPushError",
                table: "SyncStates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPushedAtUtc",
                table: "SyncStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastPushAtUtc",
                table: "ShopTerminals",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastPushError",
                table: "SyncStates");

            migrationBuilder.DropColumn(
                name: "LastPushedAtUtc",
                table: "SyncStates");

            migrationBuilder.DropColumn(
                name: "LastPushAtUtc",
                table: "ShopTerminals");
        }
    }
}
