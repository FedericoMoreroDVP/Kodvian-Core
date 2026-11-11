using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialIncomeAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "ParticipationPercentage",
                table: "Partners",
                type: "numeric(5,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.CreateTable(
                name: "FinancialIncomeAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IncomeMovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ExpensesCovered = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CapitalizedAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DistributableAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SharesConfigured = table.Column<bool>(type: "boolean", nullable: false),
                    FechaCreacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Activo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialIncomeAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialIncomeAllocations_FinancialMovements_IncomeMovemen~",
                        column: x => x.IncomeMovementId,
                        principalTable: "FinancialMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialIncomeExpenseApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseMovementId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialIncomeExpenseApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialIncomeExpenseApplications_FinancialIncomeAllocatio~",
                        column: x => x.AllocationId,
                        principalTable: "FinancialIncomeAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialIncomeExpenseApplications_FinancialMovements_Expen~",
                        column: x => x.ExpenseMovementId,
                        principalTable: "FinancialMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialIncomePartnerDistributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartnerName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Percentage = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialIncomePartnerDistributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialIncomePartnerDistributions_FinancialIncomeAllocati~",
                        column: x => x.AllocationId,
                        principalTable: "FinancialIncomeAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FinancialIncomePartnerDistributions_Partners_PartnerId",
                        column: x => x.PartnerId,
                        principalTable: "Partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialIncomeAllocations_IncomeMovementId",
                table: "FinancialIncomeAllocations",
                column: "IncomeMovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialIncomeExpenseApplications_AllocationId_ExpenseMove~",
                table: "FinancialIncomeExpenseApplications",
                columns: new[] { "AllocationId", "ExpenseMovementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialIncomeExpenseApplications_ExpenseMovementId",
                table: "FinancialIncomeExpenseApplications",
                column: "ExpenseMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialIncomePartnerDistributions_AllocationId_PartnerId",
                table: "FinancialIncomePartnerDistributions",
                columns: new[] { "AllocationId", "PartnerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialIncomePartnerDistributions_PartnerId",
                table: "FinancialIncomePartnerDistributions",
                column: "PartnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialIncomeExpenseApplications");

            migrationBuilder.DropTable(
                name: "FinancialIncomePartnerDistributions");

            migrationBuilder.DropTable(
                name: "FinancialIncomeAllocations");

            migrationBuilder.AlterColumn<decimal>(
                name: "ParticipationPercentage",
                table: "Partners",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(5,2)");
        }
    }
}
