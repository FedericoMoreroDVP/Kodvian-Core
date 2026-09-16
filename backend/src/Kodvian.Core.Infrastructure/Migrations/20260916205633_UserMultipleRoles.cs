using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kodvian.Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserMultipleRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_RoleId",
                table: "Users");

            migrationBuilder.AddColumn<Guid>(
                name: "SessionVersion",
                table: "Users",
                type: "uuid",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.Sql("INSERT INTO \"UserRoles\" (\"UserId\", \"RoleId\") SELECT \"Id\", \"RoleId\" FROM \"Users\";");
            migrationBuilder.Sql("UPDATE \"Users\" SET \"SessionVersion\" = md5(\"Id\"::text || clock_timestamp()::text)::uuid;");
            migrationBuilder.DropColumn(name: "RoleId", table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Refuse a lossy rollback once accounts have multiple roles.
            migrationBuilder.Sql(@"
DO $$ BEGIN
    IF EXISTS (SELECT u.""Id"" FROM ""Users"" u LEFT JOIN ""UserRoles"" ur ON ur.""UserId"" = u.""Id""
               GROUP BY u.""Id"" HAVING count(ur.""RoleId"") <> 1) THEN
        RAISE EXCEPTION 'Para revertir, cada usuario debe tener exactamente un rol';
    END IF;
END $$;");
            migrationBuilder.AddColumn<Guid>(name: "RoleId", table: "Users", type: "uuid", nullable: false, defaultValue: Guid.Empty);
            migrationBuilder.Sql("UPDATE \"Users\" u SET \"RoleId\" = ur.\"RoleId\" FROM \"UserRoles\" ur WHERE ur.\"UserId\" = u.\"Id\";");
            migrationBuilder.DropTable(name: "UserRoles");
            migrationBuilder.DropColumn(name: "SessionVersion", table: "Users");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Roles_RoleId",
                table: "Users",
                column: "RoleId",
                principalTable: "Roles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
