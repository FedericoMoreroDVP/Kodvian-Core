using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleCalendarIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoogleCalendarOwnerUserId",
                table: "Meetings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleEventId",
                table: "Meetings",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleSyncError",
                table: "Meetings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleSyncStatus",
                table: "Meetings",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GoogleSyncedAtUtc",
                table: "Meetings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GoogleCalendarConnections",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GoogleEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ConnectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSyncAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoogleCalendarConnections", x => x.UserId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GoogleCalendarConnections");

            migrationBuilder.DropColumn(
                name: "GoogleCalendarOwnerUserId",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "GoogleEventId",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "GoogleSyncError",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "GoogleSyncStatus",
                table: "Meetings");

            migrationBuilder.DropColumn(
                name: "GoogleSyncedAtUtc",
                table: "Meetings");
        }
    }
}
