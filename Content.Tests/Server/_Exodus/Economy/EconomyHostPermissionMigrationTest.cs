using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace Content.Tests.Server._Exodus.Economy;

[TestFixture]
public sealed class EconomyHostPermissionMigrationTest
{
    private const string PreviousMigration = "20261006171243_EconomyMarketSettings";
    private const string SqliteMigration = "20261007120000_GrantEconomyDbToHosts";
    private const string PostgresMigration = "20261007120100_GrantEconomyDbToHosts";
    private SqliteConnection _connection;
    private SqliteServerDbContext _context;

    [SetUp]
    public async Task SetUp()
    {
#if USE_SYSTEM_SQLITE
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
#endif
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteServerDbContext>().UseSqlite(_connection).Options;
        _context = new SqliteServerDbContext(options);
        await _context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
    }

    [TearDown]
    public void TearDown()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task ExistingEffectiveHostsReceivePersonalPermissionWithoutChangingExplicitChoicesOrRanks()
    {
        var hostRank = Rank("Host", "HOST");
        var economyRank = Rank("Economy host", "HOST", "ECONOMYDB");
        var ordinaryRank = Rank("Administrator", "ADMIN", "PERMISSIONS");
        var direct = Admin("Direct host", null, ("HOST", false));
        var inherited = Admin("Inherited host", hostRank);
        var rankedDirect = Admin("Direct host in ordinary rank", ordinaryRank, ("HOST", false));
        var deniedHost = Admin("Denied inherited host", hostRank, ("HOST", true));
        var negativeHost = Admin("Negative host only", null, ("HOST", true));
        var ordinary = Admin("Ordinary administrator", ordinaryRank);
        var existingPositive = Admin("Explicit economy grant", hostRank, ("ECONOMYDB", false));
        var existingNegative = Admin("Explicit economy denial", hostRank, ("ECONOMYDB", true));
        var inheritedEconomy = Admin("Inherited economy", economyRank);
        var deniedInheritedEconomy = Admin("Denied inherited economy", economyRank, ("ECONOMYDB", true));
        var suspended = Admin("Suspended host", null, ("HOST", false));
        suspended.Suspended = true;
        var deadminned = Admin("Deadminned host", hostRank);
        deadminned.Deadminned = true;
        _context.Admin.AddRange(direct, inherited, rankedDirect, deniedHost, negativeHost, ordinary,
            existingPositive, existingNegative, inheritedEconomy, deniedInheritedEconomy, suspended, deadminned);
        await _context.SaveChangesAsync();
        var originalFlags = await _context.Set<AdminFlag>().AsNoTracking().ToArrayAsync();
        var originalRankFlags = await _context.Set<AdminRankFlag>().AsNoTracking().ToArrayAsync();

        await _context.Database.MigrateAsync();

        var migrated = await _context.Admin.AsNoTracking().Include(admin => admin.Flags).ToArrayAsync();
        foreach (var original in originalFlags)
        {
            var retained = migrated.Single(admin => admin.UserId == original.AdminId).Flags
                .Single(flag => flag.Id == original.Id);
            Assert.Multiple(() =>
            {
                Assert.That(retained.Flag, Is.EqualTo(original.Flag));
                Assert.That(retained.Negative, Is.EqualTo(original.Negative));
            });
        }

        foreach (var granted in new[] { direct, inherited, rankedDirect, suspended, deadminned })
        {
            var flags = migrated.Single(admin => admin.UserId == granted.UserId).Flags
                .Where(value => value.Flag == "ECONOMYDB").ToArray();
            Assert.That(flags, Has.Length.EqualTo(1), granted.Title);
            Assert.That(flags[0].Negative, Is.False, granted.Title);
        }

        foreach (var unchanged in new[] { deniedHost, negativeHost, ordinary, inheritedEconomy })
        {
            Assert.That(migrated.Single(admin => admin.UserId == unchanged.UserId).Flags
                .Any(flag => flag.Flag == "ECONOMYDB"), Is.False, unchanged.Title);
        }

        foreach (var explicitChoice in new[] { existingPositive, existingNegative, deniedInheritedEconomy })
        {
            Assert.That(migrated.Single(admin => admin.UserId == explicitChoice.UserId).Flags
                .Count(flag => flag.Flag == "ECONOMYDB"), Is.EqualTo(1), explicitChoice.Title);
        }

        var rankFlags = await _context.Set<AdminRankFlag>().AsNoTracking().ToArrayAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rankFlags.Select(flag => (flag.Id, flag.AdminRankId, flag.Flag)),
                Is.EquivalentTo(originalRankFlags.Select(flag => (flag.Id, flag.AdminRankId, flag.Flag))));
            Assert.That(migrated.Single(admin => admin.UserId == suspended.UserId).Suspended, Is.True);
            Assert.That(migrated.Single(admin => admin.UserId == deadminned.UserId).Deadminned, Is.True);
            Assert.That(migrated.Select(admin => (admin.UserId, admin.Title, admin.AdminRankId)),
                Is.EquivalentTo(new[] { direct, inherited, rankedDirect, deniedHost, negativeHost, ordinary,
                    existingPositive, existingNegative, inheritedEconomy, deniedInheritedEconomy, suspended, deadminned }
                    .Select(admin => (admin.UserId, admin.Title, admin.AdminRankId))));
        });
    }

    [Test]
    public async Task SubsequentStartupDoesNotGrantFutureHostsOrRestoreRevokedPermission()
    {
        var hostRank = Rank("Host", "HOST");
        var existingHost = Admin("Existing host", hostRank);
        var promotedLater = Admin("Later promotion", null);
        _context.Admin.AddRange(existingHost, promotedLater);
        await _context.SaveChangesAsync();
        await _context.Database.MigrateAsync();
        _context.ChangeTracker.Clear();
        var grant = await _context.Set<AdminFlag>().SingleOrDefaultAsync(flag => flag.AdminId == existingHost.UserId
            && flag.Flag == "ECONOMYDB");
        Assert.That(grant, Is.Not.Null, "The existing host must receive the initial grant.");
        _context.Remove(grant);
        _context.Add(new AdminFlag { AdminId = promotedLater.UserId, Flag = "HOST", Negative = false });
        var futureDirectHost = Admin("Future direct host", null, ("HOST", false));
        var futureInheritedHost = Admin("Future inherited host", null);
        futureInheritedHost.AdminRankId = hostRank.Id;
        _context.Admin.AddRange(futureDirectHost, futureInheritedHost);
        await _context.SaveChangesAsync();

        await _context.Database.MigrateAsync();

        Assert.That(await _context.Set<AdminFlag>().CountAsync(flag => flag.Flag == "ECONOMYDB"), Is.Zero);
        Assert.That(await _context.Database.GetAppliedMigrationsAsync(), Does.Contain(SqliteMigration));
    }

    [Test]
    public async Task RollbackPreservesGrantedAndExistingEconomyPermissions()
    {
        var host = Admin("Host", null, ("HOST", false));
        var existing = Admin("Existing grant", null, ("ECONOMYDB", false));
        _context.Admin.AddRange(host, existing);
        await _context.SaveChangesAsync();
        await _context.Database.MigrateAsync();
        var beforeRollback = await _context.Set<AdminFlag>().AsNoTracking().ToArrayAsync();
        Assert.That(beforeRollback.Count(flag => flag.Flag == "ECONOMYDB"), Is.EqualTo(2));

        await _context.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var afterRollback = await _context.Set<AdminFlag>().AsNoTracking().ToArrayAsync();
        Assert.That(afterRollback.Select(flag => (flag.Id, flag.AdminId, flag.Flag, flag.Negative)),
            Is.EquivalentTo(beforeRollback.Select(flag => (flag.Id, flag.AdminId, flag.Flag, flag.Negative))));
    }

    [Test]
    public void BothProvidersDiscoverTheirOwnMigrationAndPostgresGeneratesTheUpgrade()
    {
        using var postgres = new PostgresServerDbContext(new DbContextOptionsBuilder<PostgresServerDbContext>()
            .UseNpgsql("Host=localhost;Database=economy_permission_migration_check").Options);
        Assert.Multiple(() =>
        {
            Assert.That(_context.Database.GetMigrations(), Does.Contain(SqliteMigration).And.Not.Contain(PostgresMigration));
            Assert.That(postgres.Database.GetMigrations(), Does.Contain(PostgresMigration).And.Not.Contain(SqliteMigration));
        });

        var script = postgres.GetService<IMigrator>().GenerateScript(
            "20261006171303_EconomyMarketSettings", PostgresMigration);
        Assert.That(script, Does.Contain("INSERT INTO admin_flag").And.Contain("'ECONOMYDB'")
            .And.Contain(PostgresMigration));
    }

    private static AdminRank Rank(string name, params string[] flags)
    {
        return new AdminRank
        {
            Name = name,
            ShortName = name,
            Admins = [],
            Flags = flags.Select(flag => new AdminRankFlag { Flag = flag }).ToList(),
        };
    }

    private static Admin Admin(string title, AdminRank rank, params (string flag, bool negative)[] flags)
    {
        return new Admin
        {
            UserId = Guid.NewGuid(),
            Title = title,
            AdminRank = rank,
            Flags = flags.Select(flag => new AdminFlag { Flag = flag.flag, Negative = flag.negative }).ToList(),
        };
    }
}
