using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiScreenFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiConversations_OwnerUserId_UpdatedAtUtc",
                table: "AiConversations");

            migrationBuilder.AddColumn<int>(
                name: "Feedback",
                table: "AiMessages",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FeedbackAtUtc",
                table: "AiMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualJson",
                table: "AiMessages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "AiConversations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_OwnerUserId_IsPinned_UpdatedAtUtc",
                table: "AiConversations",
                columns: new[] { "OwnerUserId", "IsPinned", "UpdatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiConversations_OwnerUserId_IsPinned_UpdatedAtUtc",
                table: "AiConversations");

            migrationBuilder.DropColumn(
                name: "Feedback",
                table: "AiMessages");

            migrationBuilder.DropColumn(
                name: "FeedbackAtUtc",
                table: "AiMessages");

            migrationBuilder.DropColumn(
                name: "VisualJson",
                table: "AiMessages");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "AiConversations");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_OwnerUserId_UpdatedAtUtc",
                table: "AiConversations",
                columns: new[] { "OwnerUserId", "UpdatedAtUtc" });
        }
    }
}
