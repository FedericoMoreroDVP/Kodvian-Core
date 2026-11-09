using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiApprovalAuditAndRunConfigurationVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProviderConfigurationVersion",
                table: "AiRuns",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "TermsVersion",
                table: "AiProviderConfigurationAuditEvents",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiRuns_ConversationId",
                table: "AiRuns",
                column: "ConversationId",
                unique: true,
                filter: "\"Status\" = 'Running'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiRuns_ConversationId",
                table: "AiRuns");

            migrationBuilder.DropColumn(
                name: "ProviderConfigurationVersion",
                table: "AiRuns");

            migrationBuilder.DropColumn(
                name: "TermsVersion",
                table: "AiProviderConfigurationAuditEvents");
        }
    }
}
