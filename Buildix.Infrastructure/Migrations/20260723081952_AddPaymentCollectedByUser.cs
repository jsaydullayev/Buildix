using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentCollectedByUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CollectedByUserId",
                table: "Payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CollectedByUserId",
                table: "Payments",
                column: "CollectedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Users_CollectedByUserId",
                table: "Payments",
                column: "CollectedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Users_CollectedByUserId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_Payments_CollectedByUserId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "CollectedByUserId",
                table: "Payments");
        }
    }
}
