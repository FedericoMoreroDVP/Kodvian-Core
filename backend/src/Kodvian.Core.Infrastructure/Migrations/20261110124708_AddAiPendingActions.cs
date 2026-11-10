using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiPendingActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiPendingActions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ToolName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DetailsJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResultLink = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiPendingActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiPendingActions_AiConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AiConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiPendingActions_ConversationId_CreatedAtUtc",
                table: "AiPendingActions",
                columns: new[] { "ConversationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiPendingActions_MessageId",
                table: "AiPendingActions",
                column: "MessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiPendingActions");
        }
    }
}
