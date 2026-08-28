using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncSeedProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeedAfter",
                table: "SyncStates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SeedCompletedAtUtc",
                table: "SyncStates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeedTable",
                table: "SyncStates",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeedAfter",
                table: "SyncStates");

            migrationBuilder.DropColumn(
                name: "SeedCompletedAtUtc",
                table: "SyncStates");

            migrationBuilder.DropColumn(
                name: "SeedTable",
                table: "SyncStates");
        }
    }
}
