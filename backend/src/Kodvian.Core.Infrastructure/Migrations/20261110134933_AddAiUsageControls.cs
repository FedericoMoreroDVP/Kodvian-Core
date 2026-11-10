using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EstimatedCostUsd",
                table: "AiRuns",
                type: "numeric(14,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FastModel",
                table: "AiProviderConfigurations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveDataApprovedFastModel",
                table: "AiProviderConfigurations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExecutionStartedAtUtc",
                table: "AiPendingActions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AiUsageSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    InputUsdPerMillion = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    OutputUsdPerMillion = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    FastInputUsdPerMillion = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    FastOutputUsdPerMillion = table.Column<decimal>(type: "numeric(12,4)", nullable: true),
                    MonthlyBudgetUsd = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageSettings", x => x.Id);
                    table.CheckConstraint("CK_AiUsageSettings_Singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiRuns_StartedAtUtc",
                table: "AiRuns",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageSettings");

            migrationBuilder.DropIndex(
                name: "IX_AiRuns_StartedAtUtc",
                table: "AiRuns");

            migrationBuilder.DropColumn(
                name: "EstimatedCostUsd",
                table: "AiRuns");

            migrationBuilder.DropColumn(
                name: "FastModel",
                table: "AiProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "LiveDataApprovedFastModel",
                table: "AiProviderConfigurations");

            migrationBuilder.DropColumn(
                name: "ExecutionStartedAtUtc",
                table: "AiPendingActions");
        }
    }
}
