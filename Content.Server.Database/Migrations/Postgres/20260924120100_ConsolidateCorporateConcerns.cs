// Exodus: preserve original corporate affiliations while consolidating playable companies.
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres;

[DbContext(typeof(PostgresServerDbContext))]
[Migration("20260924120100_ConsolidateCorporateConcerns")]
public sealed class ConsolidateCorporateConcerns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE exodus_concern_profile_backup (
                profile_id integer PRIMARY KEY REFERENCES profile(profile_id) ON DELETE CASCADE,
                old_company text NOT NULL,
                new_company text NOT NULL
            );
            """);
        migrationBuilder.Sql("""
            INSERT INTO exodus_concern_profile_backup (profile_id, old_company, new_company)
            SELECT profile_id, company, CASE
                WHEN company = 'AetherionDynamics' THEN 'Augok'
                WHEN company = 'UllmanIndustries' THEN 'Augok'
                WHEN company = 'CivilDefenseMilita' THEN 'Augok'
                WHEN company = 'DrakeIndustries' THEN 'DrakeBlackArmsUSA'
                WHEN company = 'blackhawkpmc' THEN 'DrakeBlackArmsUSA'
                WHEN company = 'MidnightArmsCo' THEN 'DrakeBlackArmsUSA'
                WHEN company = 'UniversalStatesOfAmerica' THEN 'DrakeBlackArmsUSA'
                WHEN company = 'HorizonEnergy' THEN 'HorizonHarmonyHammerMatter'
                WHEN company = 'HarmonyMedicalEnterprises' THEN 'HorizonHarmonyHammerMatter'
                WHEN company = 'SteelHammerManufacturing' THEN 'HorizonHarmonyHammerMatter'
                WHEN company = 'DarkMatterEnterprises' THEN 'HorizonHarmonyHammerMatter'
                WHEN company = 'TheHive' THEN 'Buno'
                WHEN company = 'paycheckbratva' THEN 'Buno'
                WHEN company = 'NosskeEienResearchandDevelopment' THEN 'Buno'
            END
            FROM profile WHERE company IN ('AetherionDynamics', 'UllmanIndustries', 'CivilDefenseMilita', 'DrakeIndustries', 'blackhawkpmc', 'MidnightArmsCo', 'UniversalStatesOfAmerica', 'HorizonEnergy', 'HarmonyMedicalEnterprises', 'SteelHammerManufacturing', 'DarkMatterEnterprises', 'TheHive', 'paycheckbratva', 'NosskeEienResearchandDevelopment');
            """);
        migrationBuilder.Sql("""
            UPDATE profile SET company = (
                SELECT new_company FROM exodus_concern_profile_backup
                WHERE profile_id = profile.profile_id
            ) WHERE profile_id IN (SELECT profile_id FROM exodus_concern_profile_backup);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE exodus_concern_member_backup (
                player_user_id uuid NOT NULL,
                old_company_id text NOT NULL,
                owner boolean NOT NULL,
                new_company_id text NOT NULL,
                PRIMARY KEY (player_user_id, old_company_id),
                FOREIGN KEY (player_user_id) REFERENCES player(user_id) ON DELETE CASCADE
            );
            """);
        migrationBuilder.Sql("""
            INSERT INTO exodus_concern_member_backup (player_user_id, old_company_id, owner, new_company_id)
            SELECT player_user_id, company_id, owner, CASE
                WHEN company_id = 'AetherionDynamics' THEN 'Augok'
                WHEN company_id = 'UllmanIndustries' THEN 'Augok'
                WHEN company_id = 'CivilDefenseMilita' THEN 'Augok'
                WHEN company_id = 'DrakeIndustries' THEN 'DrakeBlackArmsUSA'
                WHEN company_id = 'blackhawkpmc' THEN 'DrakeBlackArmsUSA'
                WHEN company_id = 'MidnightArmsCo' THEN 'DrakeBlackArmsUSA'
                WHEN company_id = 'UniversalStatesOfAmerica' THEN 'DrakeBlackArmsUSA'
                WHEN company_id = 'HorizonEnergy' THEN 'HorizonHarmonyHammerMatter'
                WHEN company_id = 'HarmonyMedicalEnterprises' THEN 'HorizonHarmonyHammerMatter'
                WHEN company_id = 'SteelHammerManufacturing' THEN 'HorizonHarmonyHammerMatter'
                WHEN company_id = 'DarkMatterEnterprises' THEN 'HorizonHarmonyHammerMatter'
                WHEN company_id = 'TheHive' THEN 'Buno'
                WHEN company_id = 'paycheckbratva' THEN 'Buno'
                WHEN company_id = 'NosskeEienResearchandDevelopment' THEN 'Buno'
            END
            FROM company_members WHERE company_id IN ('AetherionDynamics', 'UllmanIndustries', 'CivilDefenseMilita', 'DrakeIndustries', 'blackhawkpmc', 'MidnightArmsCo', 'UniversalStatesOfAmerica', 'HorizonEnergy', 'HarmonyMedicalEnterprises', 'SteelHammerManufacturing', 'DarkMatterEnterprises', 'TheHive', 'paycheckbratva', 'NosskeEienResearchandDevelopment');
            """);
        migrationBuilder.Sql("""
            INSERT INTO company_members (player_user_id, company_id, owner)
            SELECT player_user_id, new_company_id, BOOL_OR(owner)
            FROM exodus_concern_member_backup
            GROUP BY player_user_id, new_company_id
            ON CONFLICT (player_user_id, company_id)
            DO UPDATE SET owner = company_members.owner OR EXCLUDED.owner;
            """);
        migrationBuilder.Sql("""
            DELETE FROM company_members WHERE company_id IN ('AetherionDynamics', 'UllmanIndustries', 'CivilDefenseMilita', 'DrakeIndustries', 'blackhawkpmc', 'MidnightArmsCo', 'UniversalStatesOfAmerica', 'HorizonEnergy', 'HarmonyMedicalEnterprises', 'SteelHammerManufacturing', 'DarkMatterEnterprises', 'TheHive', 'paycheckbratva', 'NosskeEienResearchandDevelopment');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Exodus: restore old memberships without deleting concern grants made after migration.
        migrationBuilder.Sql("""
            UPDATE profile SET company = (
                SELECT old_company FROM exodus_concern_profile_backup
                WHERE profile_id = profile.profile_id
            ) WHERE EXISTS (
                SELECT 1 FROM exodus_concern_profile_backup AS backup
                WHERE backup.profile_id = profile.profile_id AND backup.new_company = profile.company
            );
            """);
        migrationBuilder.Sql("""
            INSERT INTO company_members (player_user_id, company_id, owner)
            SELECT backup.player_user_id, backup.old_company_id, backup.owner AND current.owner
            FROM exodus_concern_member_backup AS backup
            JOIN company_members AS current ON current.player_user_id = backup.player_user_id
                AND current.company_id = backup.new_company_id
            ON CONFLICT (player_user_id, company_id) DO NOTHING;
            """);
        migrationBuilder.Sql("DROP TABLE exodus_concern_member_backup;");
        migrationBuilder.Sql("DROP TABLE exodus_concern_profile_backup;");
    }
}
