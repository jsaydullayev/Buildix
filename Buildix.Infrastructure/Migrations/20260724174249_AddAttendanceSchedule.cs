using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Standart grafik 08:00–20:00 · kech 08:15 — mavjud do'konlar uchun
            // xatti-harakat o'zgarmasligi uchun default shu qiymatlar.
            migrationBuilder.AddColumn<TimeOnly>(
                name: "LateThreshold",
                table: "MarketSettings",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(8, 15, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WorkDayEnd",
                table: "MarketSettings",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(20, 0, 0));

            migrationBuilder.AddColumn<TimeOnly>(
                name: "WorkDayStart",
                table: "MarketSettings",
                type: "time without time zone",
                nullable: false,
                defaultValue: new TimeOnly(8, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LateThreshold",
                table: "MarketSettings");

            migrationBuilder.DropColumn(
                name: "WorkDayEnd",
                table: "MarketSettings");

            migrationBuilder.DropColumn(
                name: "WorkDayStart",
                table: "MarketSettings");
        }
    }
}
