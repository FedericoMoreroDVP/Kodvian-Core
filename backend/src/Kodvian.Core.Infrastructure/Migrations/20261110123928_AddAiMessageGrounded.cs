using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiMessageGrounded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Grounded",
                table: "AiMessages",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Grounded",
                table: "AiMessages");
        }
    }
}
