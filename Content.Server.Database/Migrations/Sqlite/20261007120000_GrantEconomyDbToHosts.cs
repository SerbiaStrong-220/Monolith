// Exodus: grant the economy permission once to existing host accounts.
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Content.Server.Database.Migrations.Sqlite;

[DbContext(typeof(SqliteServerDbContext))]
[Migration("20261007120000_GrantEconomyDbToHosts")]
public sealed class GrantEconomyDbToHosts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Use effective HOST permissions, including personal overrides. Do not change ranks or existing economy choices.
        migrationBuilder.Sql("""
            INSERT INTO admin_flag (admin_id, flag, negative)
            SELECT administrator.user_id, 'ECONOMYDB', FALSE
            FROM admin AS administrator
            WHERE (
                EXISTS (
                    SELECT 1
                    FROM admin_flag AS personal_host
                    WHERE personal_host.admin_id = administrator.user_id
                      AND personal_host.flag = 'HOST'
                      AND NOT personal_host.negative
                )
                OR EXISTS (
                    SELECT 1
                    FROM admin_rank_flag AS rank_host
                    WHERE rank_host.admin_rank_id = administrator.admin_rank_id
                      AND rank_host.flag = 'HOST'
                )
            )
            AND NOT EXISTS (
                SELECT 1
                FROM admin_flag AS denied_host
                WHERE denied_host.admin_id = administrator.user_id
                  AND denied_host.flag = 'HOST'
                  AND denied_host.negative
            )
            AND NOT EXISTS (
                SELECT 1
                FROM admin_flag AS personal_economy
                WHERE personal_economy.admin_id = administrator.user_id
                  AND personal_economy.flag = 'ECONOMYDB'
            )
            AND NOT EXISTS (
                SELECT 1
                FROM admin_rank_flag AS rank_economy
                WHERE rank_economy.admin_rank_id = administrator.admin_rank_id
                  AND rank_economy.flag = 'ECONOMYDB'
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep granted permissions: they cannot be distinguished safely from later manual permission changes.
    }
}
