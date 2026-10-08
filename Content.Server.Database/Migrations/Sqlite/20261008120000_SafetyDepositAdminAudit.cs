// Exodus safety deposit administration
using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Sqlite;

public partial class SafetyDepositAdminAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "safety_deposit_admin_audit",
            columns: table => new
            {
                id = table.Column<Guid>(type: "TEXT", nullable: false),
                action = table.Column<string>(type: "TEXT", nullable: false),
                admin_name = table.Column<string>(type: "TEXT", nullable: false),
                admin_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                box_id = table.Column<Guid>(type: "TEXT", nullable: false),
                character_index = table.Column<int>(type: "INTEGER", nullable: false),
                created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                details = table.Column<string>(type: "TEXT", nullable: false),
                item_data = table.Column<string>(type: "TEXT", nullable: true),
                owner_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                result = table.Column<string>(type: "TEXT", nullable: false),
                round_id = table.Column<int>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_deposit_admin_audit", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_safety_deposit_admin_audit_box_id_created_at",
            table: "safety_deposit_admin_audit",
            columns: new[] { "box_id", "created_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "safety_deposit_admin_audit");
    }
}
