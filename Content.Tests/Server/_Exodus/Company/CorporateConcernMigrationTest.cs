using System;
using Content.Server.Database.Migrations.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using NUnit.Framework;

namespace Content.Tests.Server._Exodus.Company;

[TestFixture]
public sealed class CorporateConcernMigrationTest
{
    [Test]
    public void ExistingCharactersAndMembershipsSurviveConsolidationAndRollback()
    {
#if USE_SYSTEM_SQLITE
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
#endif
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        Execute(connection, "PRAGMA foreign_keys = ON;");
        Execute(connection, "CREATE TABLE player (user_id TEXT PRIMARY KEY);");
        Execute(connection, "INSERT INTO player VALUES ('player');");
        Execute(connection, "CREATE TABLE profile (profile_id INTEGER PRIMARY KEY, company TEXT NOT NULL);");
        Execute(connection, "CREATE TABLE company_members (player_user_id TEXT NOT NULL, company_id TEXT NOT NULL, owner INTEGER NOT NULL, PRIMARY KEY (player_user_id, company_id));");
        Execute(connection, "INSERT INTO profile VALUES (1, 'SteelHammerManufacturing'), (2, 'NosskeEienResearchandDevelopment'), (3, 'TheViperGroup');");
        Execute(connection, "INSERT INTO company_members VALUES ('player', 'SteelHammerManufacturing', 0), ('player', 'DarkMatterEnterprises', 1), ('player', 'HorizonHarmonyHammerMatter', 0), ('player', 'TheViperGroup', 1);");

        var legacyCompanies = new[]
        {
            "AetherionDynamics", "UllmanIndustries", "CivilDefenseMilita", "DrakeIndustries",
            "blackhawkpmc", "MidnightArmsCo", "UniversalStatesOfAmerica", "HorizonEnergy",
            "HarmonyMedicalEnterprises", "SteelHammerManufacturing", "DarkMatterEnterprises",
            "TheHive", "paycheckbratva", "NosskeEienResearchandDevelopment",
        };
        var expectedConcerns = new[]
        {
            "Augok", "Augok", "Augok", "DrakeBlackArmsUSA",
            "DrakeBlackArmsUSA", "DrakeBlackArmsUSA", "DrakeBlackArmsUSA", "HorizonHarmonyHammerMatter",
            "HorizonHarmonyHammerMatter", "HorizonHarmonyHammerMatter", "HorizonHarmonyHammerMatter",
            "Buno", "Buno", "Buno",
        };
        for (var i = 0; i < legacyCompanies.Length; i++)
        {
            var legacyCompany = legacyCompanies[i];
            Execute(connection, $"INSERT INTO profile VALUES ({i + 100}, '{legacyCompany}');");
            if (legacyCompany is not ("SteelHammerManufacturing" or "DarkMatterEnterprises"))
                Execute(connection, $"INSERT INTO company_members VALUES ('player', '{legacyCompany}', 0);");
        }

        var migration = new ConsolidateCorporateConcerns();
        foreach (var operation in migration.UpOperations)
            Execute(connection, ((SqlOperation)operation).Sql);

        Assert.Multiple(() =>
        {
            Assert.That(Scalar(connection, "SELECT company FROM profile WHERE profile_id = 1"), Is.EqualTo("HorizonHarmonyHammerMatter"));
            Assert.That(Scalar(connection, "SELECT company FROM profile WHERE profile_id = 2"), Is.EqualTo("Buno"));
            Assert.That(Scalar(connection, "SELECT company FROM profile WHERE profile_id = 3"), Is.EqualTo("TheViperGroup"));
            Assert.That(Scalar(connection, "SELECT COUNT(*) FROM company_members WHERE player_user_id = 'player' AND company_id = 'HorizonHarmonyHammerMatter'"), Is.EqualTo(1L));
            Assert.That(Scalar(connection, "SELECT owner FROM company_members WHERE player_user_id = 'player' AND company_id = 'HorizonHarmonyHammerMatter'"), Is.EqualTo(1L));
            Assert.That(Scalar(connection, "SELECT owner FROM company_members WHERE player_user_id = 'player' AND company_id = 'TheViperGroup'"), Is.EqualTo(1L));
            Assert.That(Scalar(connection, "SELECT COUNT(*) FROM company_members WHERE player_user_id = 'player' AND company_id IN ('Augok', 'DrakeBlackArmsUSA', 'HorizonHarmonyHammerMatter', 'Buno')"), Is.EqualTo(4L));
        });

        for (var i = 0; i < legacyCompanies.Length; i++)
        {
            Assert.That(Scalar(connection, $"SELECT company FROM profile WHERE profile_id = {i + 100}"),
                Is.EqualTo(expectedConcerns[i]), legacyCompanies[i]);
            Assert.That(Scalar(connection, $"SELECT COUNT(*) FROM company_members WHERE company_id = '{legacyCompanies[i]}'"),
                Is.EqualTo(0L), legacyCompanies[i]);
        }

        foreach (var operation in migration.DownOperations)
            Execute(connection, ((SqlOperation)operation).Sql);

        Assert.Multiple(() =>
        {
            Assert.That(Scalar(connection, "SELECT company FROM profile WHERE profile_id = 1"), Is.EqualTo("SteelHammerManufacturing"));
            Assert.That(Scalar(connection, "SELECT company FROM profile WHERE profile_id = 2"), Is.EqualTo("NosskeEienResearchandDevelopment"));
            Assert.That(Scalar(connection, "SELECT owner FROM company_members WHERE player_user_id = 'player' AND company_id = 'SteelHammerManufacturing'"), Is.EqualTo(0L));
            Assert.That(Scalar(connection, "SELECT owner FROM company_members WHERE player_user_id = 'player' AND company_id = 'DarkMatterEnterprises'"), Is.EqualTo(1L));
            Assert.That(Scalar(connection, "SELECT owner FROM company_members WHERE player_user_id = 'player' AND company_id = 'HorizonHarmonyHammerMatter'"), Is.EqualTo(1L));
        });

        for (var i = 0; i < legacyCompanies.Length; i++)
            Assert.That(Scalar(connection, $"SELECT company FROM profile WHERE profile_id = {i + 100}"),
                Is.EqualTo(legacyCompanies[i]), legacyCompanies[i]);
    }

    [Test]
    public void RollbackPreservesLaterChoicesRevocationsAndDeletions()
    {
#if USE_SYSTEM_SQLITE
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
#endif
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Execute(connection, "PRAGMA foreign_keys = ON;");
        Execute(connection, "CREATE TABLE player (user_id TEXT PRIMARY KEY);");
        Execute(connection, "CREATE TABLE profile (profile_id INTEGER PRIMARY KEY, company TEXT NOT NULL);");
        Execute(connection, "CREATE TABLE company_members (player_user_id TEXT REFERENCES player(user_id) ON DELETE CASCADE, company_id TEXT NOT NULL, owner INTEGER NOT NULL, PRIMARY KEY (player_user_id, company_id));");
        Execute(connection, "INSERT INTO player VALUES ('revoked'), ('demoted'), ('deleted');");
        Execute(connection, "INSERT INTO profile VALUES (1, 'TheHive'), (2, 'UllmanIndustries');");
        Execute(connection, "INSERT INTO company_members VALUES ('revoked', 'TheHive', 1), ('demoted', 'UllmanIndustries', 1), ('deleted', 'DrakeIndustries', 1);");

        var migration = new ConsolidateCorporateConcerns();
        foreach (var operation in migration.UpOperations)
            Execute(connection, ((SqlOperation)operation).Sql);

        Execute(connection, "UPDATE profile SET company = 'MMC' WHERE profile_id = 1;");
        Execute(connection, "DELETE FROM profile WHERE profile_id = 2;");
        Execute(connection, "INSERT INTO profile VALUES (2, 'Augok');");
        Execute(connection, "DELETE FROM company_members WHERE player_user_id = 'revoked';");
        Execute(connection, "UPDATE company_members SET owner = 0 WHERE player_user_id = 'demoted';");
        Execute(connection, "DELETE FROM player WHERE user_id = 'deleted';");

        foreach (var operation in migration.DownOperations)
            Execute(connection, ((SqlOperation)operation).Sql);

        Assert.Multiple(() =>
        {
            Assert.That(Scalar(connection, "SELECT company FROM profile WHERE profile_id = 1"), Is.EqualTo("MMC"));
            Assert.That(Scalar(connection, "SELECT company FROM profile WHERE profile_id = 2"), Is.EqualTo("Augok"));
            Assert.That(Scalar(connection, "SELECT COUNT(*) FROM company_members WHERE player_user_id = 'revoked'"), Is.EqualTo(0L));
            Assert.That(Scalar(connection, "SELECT COUNT(*) FROM company_members WHERE player_user_id = 'deleted'"), Is.EqualTo(0L));
            Assert.That(Scalar(connection, "SELECT owner FROM company_members WHERE player_user_id = 'demoted' AND company_id = 'UllmanIndustries'"), Is.EqualTo(0L));
        });
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static object Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()!;
    }
}
