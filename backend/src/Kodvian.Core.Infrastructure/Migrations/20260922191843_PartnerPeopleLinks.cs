using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PartnerPeopleLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeveloperId",
                table: "Partners",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Partners",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "Partners",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_DeveloperId",
                table: "Partners",
                column: "DeveloperId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_UserId",
                table: "Partners",
                column: "UserId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Partners_OnePersonSource",
                table: "Partners",
                sql: "\"DeveloperId\" IS NULL OR \"UserId\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Partners_Developers_DeveloperId",
                table: "Partners",
                column: "DeveloperId",
                principalTable: "Developers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Partners_Users_UserId",
                table: "Partners",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM ""Partners"" WHERE ""DeveloperId"" IS NOT NULL OR ""UserId"" IS NOT NULL OR ""Email"" IS NOT NULL) THEN
        RAISE EXCEPTION 'No se puede revertir sin perder vínculos o datos de socios. Se requiere una migración de datos explícita.';
    END IF;
END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_Partners_Developers_DeveloperId",
                table: "Partners");

            migrationBuilder.DropForeignKey(
                name: "FK_Partners_Users_UserId",
                table: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Partners_DeveloperId",
                table: "Partners");

            migrationBuilder.DropIndex(
                name: "IX_Partners_UserId",
                table: "Partners");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Partners_OnePersonSource",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "DeveloperId",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Partners");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "Partners");
        }
    }
}
