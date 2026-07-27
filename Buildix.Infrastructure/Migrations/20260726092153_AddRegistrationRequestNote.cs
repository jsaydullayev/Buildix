using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Buildix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationRequestNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "RegistrationRequests",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "RegistrationRequests");
        }
    }
}
