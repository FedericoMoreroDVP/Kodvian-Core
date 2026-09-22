using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinanceHistoryCurrencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "ProjectDeveloperContracts",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "FinancialMovements",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "ARS");

            migrationBuilder.AddColumn<Guid>(
                name: "ExchangeId",
                table: "FinancialMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Funding",
                table: "FinancialMovements",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Empresa");

            migrationBuilder.AddColumn<string>(
                name: "Nature",
                table: "FinancialMovements",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "Operacion");

            migrationBuilder.AddColumn<Guid>(
                name: "PartnerId",
                table: "FinancialMovements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "SettlementDate",
                table: "FinancialMovements",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SettlementDateEstimated",
                table: "FinancialMovements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "FinancialMovements",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "AppliedAmount",
                table: "DeveloperPayments",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppliedCurrency",
                table: "DeveloperPayments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "DeveloperPayments",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FinancialMovementId",
                table: "DeveloperPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "DeveloperPayments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "DeveloperPayments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "FinanceSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    OpeningArs = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    OpeningUsd = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    HistoryComplete = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceSettings", x => x.Id);
                    table.CheckConstraint("CK_FinanceSettings_Singleton", "\"Id\" = 1");
                });

            // Existing financial movements were confirmed as ARS. Historical settlement
            // dates use the recorded date as a provisional reference, visibly marked for review.
            migrationBuilder.Sql("UPDATE \"FinancialMovements\" SET \"SettlementDate\" = \"MovementDate\", \"SettlementDateEstimated\" = TRUE WHERE \"Status\" IN (2, 3);");
            migrationBuilder.Sql("UPDATE \"FinancialMovements\" SET \"Version\" = md5(\"Id\"::text || clock_timestamp()::text)::uuid;");
            migrationBuilder.Sql("UPDATE \"DeveloperPayments\" SET \"Version\" = md5(\"Id\"::text || clock_timestamp()::text)::uuid;");
            // No backfill of payment/contract currencies or financial links: those are
            // deliberately reviewed by the user, without generating historical expenses.

            migrationBuilder.CreateTable(
                name: "Partners",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMovements_Currency_SettlementDate",
                table: "FinancialMovements",
                columns: new[] { "Currency", "SettlementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMovements_ExchangeId",
                table: "FinancialMovements",
                column: "ExchangeId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialMovements_PartnerId",
                table: "FinancialMovements",
                column: "PartnerId");

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperPayments_FinancialMovementId",
                table: "DeveloperPayments",
                column: "FinancialMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeveloperPayments_RequestId",
                table: "DeveloperPayments",
                column: "RequestId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DeveloperPayments_FinancialMovements_FinancialMovementId",
                table: "DeveloperPayments",
                column: "FinancialMovementId",
                principalTable: "FinancialMovements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialMovements_Partners_PartnerId",
                table: "FinancialMovements",
                column: "PartnerId",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM ""FinancialMovements"" WHERE ""Currency"" <> 'ARS' OR ""Nature"" <> 'Operacion' OR ""Funding"" <> 'Empresa' OR ""SettlementDate"" <> ""MovementDate"")
       OR EXISTS (SELECT 1 FROM ""DeveloperPayments"" WHERE ""Currency"" IS NOT NULL OR ""FinancialMovementId"" IS NOT NULL)
       OR EXISTS (SELECT 1 FROM ""ProjectDeveloperContracts"" WHERE ""Currency"" IS NOT NULL)
       OR EXISTS (SELECT 1 FROM ""Partners"") OR EXISTS (SELECT 1 FROM ""FinanceSettings"") THEN
       RAISE EXCEPTION 'No se puede revertir sin perder monedas, saldos o vínculos financieros. Se requiere una migración de datos explícita.';
    END IF;
END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_DeveloperPayments_FinancialMovements_FinancialMovementId",
                table: "DeveloperPayments");

            migrationBuilder.DropForeignKey(
                name: "FK_FinancialMovements_Partners_PartnerId",
                table: "FinancialMovements");

            migrationBuilder.DropTable(
                name: "FinanceSettings");

            migrationBuilder.DropTable(
                name: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_FinancialMovements_Currency_SettlementDate",
                table: "FinancialMovements");

            migrationBuilder.DropIndex(
                name: "IX_FinancialMovements_ExchangeId",
                table: "FinancialMovements");

            migrationBuilder.DropIndex(
                name: "IX_FinancialMovements_PartnerId",
                table: "FinancialMovements");

            migrationBuilder.DropIndex(
                name: "IX_DeveloperPayments_FinancialMovementId",
                table: "DeveloperPayments");

            migrationBuilder.DropIndex(
                name: "IX_DeveloperPayments_RequestId",
                table: "DeveloperPayments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "ProjectDeveloperContracts");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "FinancialMovements");

            migrationBuilder.DropColumn(
                name: "ExchangeId",
                table: "FinancialMovements");

            migrationBuilder.DropColumn(
                name: "Funding",
                table: "FinancialMovements");

            migrationBuilder.DropColumn(
                name: "Nature",
                table: "FinancialMovements");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                table: "FinancialMovements");

            migrationBuilder.DropColumn(
                name: "SettlementDate",
                table: "FinancialMovements");

            migrationBuilder.DropColumn(
                name: "SettlementDateEstimated",
                table: "FinancialMovements");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "FinancialMovements");

            migrationBuilder.DropColumn(
                name: "AppliedAmount",
                table: "DeveloperPayments");

            migrationBuilder.DropColumn(
                name: "AppliedCurrency",
                table: "DeveloperPayments");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "DeveloperPayments");

            migrationBuilder.DropColumn(
                name: "FinancialMovementId",
                table: "DeveloperPayments");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "DeveloperPayments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "DeveloperPayments");
        }
    }
}
