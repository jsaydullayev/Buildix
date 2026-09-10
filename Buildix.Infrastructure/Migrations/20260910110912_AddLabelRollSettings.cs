using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLabelRollSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LabelGapMm",
                table: "MarketSettings",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: false,
                defaultValue: 2m);

            migrationBuilder.AddColumn<decimal>(
                name: "LabelHeightMm",
                table: "MarketSettings",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: false,
                defaultValue: 40m);

            migrationBuilder.AddColumn<decimal>(
                name: "LabelOffsetMm",
                table: "MarketSettings",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LabelWidthMm",
                table: "MarketSettings",
                type: "numeric(5,1)",
                precision: 5,
                scale: 1,
                nullable: false,
                defaultValue: 58m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LabelGapMm",
                table: "MarketSettings");

            migrationBuilder.DropColumn(
                name: "LabelHeightMm",
                table: "MarketSettings");

            migrationBuilder.DropColumn(
                name: "LabelOffsetMm",
                table: "MarketSettings");

            migrationBuilder.DropColumn(
                name: "LabelWidthMm",
                table: "MarketSettings");
        }
    }
}
