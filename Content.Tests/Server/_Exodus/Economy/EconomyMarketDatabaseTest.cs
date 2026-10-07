// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.CCVar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Timing;
using Robust.UnitTesting;

namespace Content.Tests.Server._Exodus.Economy;

[TestFixture]
public sealed class EconomyMarketDatabaseTest
{
    private SqliteConnection _connection;
    private DbContextOptions<SqliteServerDbContext> _options;
    private IConfigurationManager _configuration;
    private LogManager _logs;
    private ServerDbSqlite _database;

    [SetUp]
    public async Task SetUp()
    {
#if USE_SYSTEM_SQLITE
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
#endif
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<SqliteServerDbContext>().UseSqlite(_connection).Options;
        _logs = new LogManager();
        _configuration = MockInterfaces.MakeConfigurationManager(new Mock<IGameTiming>().Object, _logs,
            loadCvarsFromTypes: [typeof(CCVars)]);
        _configuration.SetCVar(CCVars.DatabaseSqliteDelay, 0);
        _database = OpenDatabase();
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
        _logs.Dispose();
    }

    [Test]
    public async Task DuplicateQuotesInOneBatchKeepLastValue()
    {
        await _database.UpsertEconomyMarketQuotes([("proto:duplicate", 2.0, 0f), ("proto:duplicate", 3.0, 0.5f)]);
        var quotes = await _database.GetAllEconomyMarketQuotes();

        Assert.Multiple(() =>
        {
            Assert.That(quotes, Has.Count.EqualTo(1));
            Assert.That(quotes[0].Factor, Is.EqualTo(3.0));
            Assert.That(quotes[0].Trend, Is.EqualTo(0.5f));
            Assert.That(quotes[0].UpdatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    [Test]
    public async Task BatchedQuotesSurviveReopeningAndDeleteAcrossBatchBoundaries()
    {
        var quotes = new List<(string, double, float)>();
        var keys = new List<string>();
        for (var i = 0; i < 1001; i++)
        {
            quotes.Add(($"proto:batch-{i}", 2.0, 0.25f));
            keys.Add($"proto:batch-{i}");
        }

        await _database.UpsertEconomyMarketQuotes(quotes);
        var reopened = OpenDatabase();
        Assert.That(await reopened.GetAllEconomyMarketQuotes(), Has.Count.EqualTo(1001));
        await reopened.UpsertEconomyMarketQuotes([("proto:batch-500", 4.0, 0f), ("proto:survivor", 5.0, 0f)]);
        Assert.That((await reopened.GetAllEconomyMarketQuotes()).Single(q => q.MarketKey == "proto:batch-500").Factor,
            Is.EqualTo(4.0));
        await reopened.DeleteEconomyMarketQuotes(keys);
        Assert.That((await reopened.GetAllEconomyMarketQuotes()).Single().MarketKey, Is.EqualTo("proto:survivor"));
        await reopened.ClearEconomyMarketQuotes();
        Assert.That(await reopened.GetAllEconomyMarketQuotes(), Is.Empty);
    }

    [Test]
    public void BothDatabaseModelsMatchTheirMigrationSnapshots()
    {
        using var sqlite = new SqliteServerDbContext(_options);
        using var postgres = new PostgresServerDbContext(new DbContextOptionsBuilder<PostgresServerDbContext>()
            .UseNpgsql("Host=localhost;Database=economy_model_check").Options);
        Assert.Multiple(() =>
        {
            Assert.That(sqlite.Database.HasPendingModelChanges(), Is.False);
            Assert.That(postgres.Database.HasPendingModelChanges(), Is.False);
        });
    }

    [Test]
    public async Task FailedReplacementRollsBackClearAndEarlierInserts()
    {
        await _database.UpsertEconomyMarketQuotes([("proto:original", 2.0, 0f)]);
        await using (var context = new SqliteServerDbContext(_options))
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_economy_insert BEFORE INSERT ON economy_market_quotes
                WHEN NEW.market_key = 'proto:failure'
                BEGIN SELECT RAISE(ABORT, 'Injected write failure'); END;
                """);
        }

        Assert.ThrowsAsync<DbUpdateException>(async () => await _database.SaveEconomyMarketQuotes(
            [("proto:accepted", 3.0, 0f), ("proto:failure", 4.0, 0f)], [], true));

        var quotes = await _database.GetAllEconomyMarketQuotes();
        Assert.Multiple(() =>
        {
            Assert.That(quotes, Has.Count.EqualTo(1));
            Assert.That(quotes[0].MarketKey, Is.EqualTo("proto:original"));
            Assert.That(quotes[0].Factor, Is.EqualTo(2.0));
        });
    }

    [Test]
    public async Task EconomyMigrationCanBeAppliedAfterNewerUpstreamMigrations()
    {
        await using (var context = new SqliteServerDbContext(_options))
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE economy_market_quotes;");
            await context.Database.ExecuteSqlRawAsync("""
                DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260719010000_EconomyMarketQuotes';
                """);
        }

        var reopened = OpenDatabase();
        await reopened.UpsertEconomyMarketQuotes([("proto:after-upgrade", 2.0, 0f)]);
        Assert.That((await reopened.GetAllEconomyMarketQuotes()).Single().MarketKey, Is.EqualTo("proto:after-upgrade"));
    }

    private ServerDbSqlite OpenDatabase()
    {
        return new ServerDbSqlite(() => _options, true, _configuration, true, _logs.GetSawmill("economy-test"));
    }
}
