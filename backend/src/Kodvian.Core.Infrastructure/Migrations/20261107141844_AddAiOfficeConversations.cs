using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiOfficeConversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LiveDataApprovalTermsVersion",
                table: "AiProviderConfigurations",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LiveDataApprovedAtUtc",
                table: "AiProviderConfigurations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LiveDataApprovedByUserId",
                table: "AiProviderConfigurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveDataApprovedModel",
                table: "AiProviderConfigurations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveDataApprovedProvider",
                table: "AiProviderConfigurations",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ToolName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Decision = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiConversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    SourcesJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiMessages_AiConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AiConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TraceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ToolCallCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiRuns_AiConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "AiConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiAuditEvents_ActorUserId",
                table: "AiAuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiAuditEvents_CreatedAtUtc",
                table: "AiAuditEvents",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiConversations_OwnerUserId_UpdatedAtUtc",
                table: "AiConversations",
                columns: new[] { "OwnerUserId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiMessages_ConversationId_ClientRequestId",
                table: "AiMessages",
                columns: new[] { "ConversationId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiMessages_ConversationId_CreatedAtUtc",
                table: "AiMessages",
                columns: new[] { "ConversationId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiMessages_RunId",
                table: "AiMessages",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_AiRuns_ConversationId_ClientRequestId",
                table: "AiRuns",
                columns: new[] { "ConversationId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiRuns_ConversationId_StartedAtUtc",
                table: "AiRuns",
                columns: new[] { "ConversationId", "StartedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiAuditEvents");

            migrationBuilder.DropTable(
                name: "AiMessages");

            migrationBuilder.DropTable(
                name: "AiRuns");

            migrationBuilder.DropTable(
                name: "AiConversations");

            migrationBuilder.DropColumn(
                name: "LiveDataApprovalTermsVersion",
                table: "AiProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "LiveDataApprovedAtUtc",
                table: "AiProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "LiveDataApprovedByUserId",
                table: "AiProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "LiveDataApprovedModel",
                table: "AiProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "LiveDataApprovedProvider",
                table: "AiProviderConfigurations");
        }
    }
}
