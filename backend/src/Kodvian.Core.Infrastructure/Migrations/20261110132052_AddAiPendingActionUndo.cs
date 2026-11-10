using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiPendingActionUndo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ResultEntityId",
                table: "AiPendingActions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UndoJson",
                table: "AiPendingActions",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UndoneAtUtc",
                table: "AiPendingActions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultEntityId",
                table: "AiPendingActions");

            migrationBuilder.DropColumn(
                name: "UndoJson",
                table: "AiPendingActions");

            migrationBuilder.DropColumn(
                name: "UndoneAtUtc",
                table: "AiPendingActions");
        }
    }
}
