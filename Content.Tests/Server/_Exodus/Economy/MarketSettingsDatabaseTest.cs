// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.IO;
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
public sealed class MarketSettingsDatabaseTest
{
    private const string InitialSettings = """{"version":1,"enabled":true}""";
    private const string UpdatedSettings = """{"version":1,"enabled":false}""";

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
        _configuration.SetCVar(CCVars.DatabaseSqliteConcurrency, 1);
        _database = OpenDatabase();
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
        _logs.Dispose();
    }

    [Test]
    public async Task SettingsAndRevisionSurviveReopening()
    {
        Assert.That(await _database.GetEconomyMarketSettings(), Is.Null);
        Assert.That(await _database.TrySaveEconomyMarketSettings(0, InitialSettings), Is.True);

        var reopened = OpenDatabase();
        Assert.That(await reopened.GetEconomyMarketSettings(), Is.EqualTo((1L, InitialSettings)));
        Assert.That(await reopened.TrySaveEconomyMarketSettings(1, UpdatedSettings), Is.True);
        Assert.That(await OpenDatabase().GetEconomyMarketSettings(), Is.EqualTo((2L, UpdatedSettings)));
    }

    [Test]
    public async Task StaleWritersCannotCreateOrOverwriteSettings()
    {
        Assert.That(await _database.TrySaveEconomyMarketSettings(1, UpdatedSettings), Is.False);
        Assert.That(await _database.GetEconomyMarketSettings(), Is.Null);
        Assert.That(await _database.TrySaveEconomyMarketSettings(0, InitialSettings), Is.True);

        var staleWriter = OpenDatabase();
        Assert.That(await staleWriter.TrySaveEconomyMarketSettings(0, UpdatedSettings), Is.False);
        Assert.That(await _database.TrySaveEconomyMarketSettings(1, UpdatedSettings), Is.True);
        Assert.That(await staleWriter.TrySaveEconomyMarketSettings(1, InitialSettings), Is.False);
        Assert.That(await staleWriter.GetEconomyMarketSettings(), Is.EqualTo((2L, UpdatedSettings)));
    }

    [TestCase(0L)]
    [TestCase(1L)]
    public async Task ConcurrentWritersPersistExactlyOneRevision(long expectedRevision)
    {
        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"market-settings-{Guid.NewGuid():N}.sqlite");
        var options = new DbContextOptionsBuilder<SqliteServerDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            var first = OpenDatabase(options, false);
            var second = OpenDatabase(options, false);
            if (expectedRevision == 1)
                Assert.That(await first.TrySaveEconomyMarketSettings(0, InitialSettings), Is.True);

            var results = await Task.WhenAll(
                Task.Run(() => first.TrySaveEconomyMarketSettings(expectedRevision, InitialSettings)),
                Task.Run(() => second.TrySaveEconomyMarketSettings(expectedRevision, UpdatedSettings)));

            Assert.That(results[0] ^ results[1], Is.True, "Only one writer may accept the same revision.");
            var expectedSettings = results[0] ? InitialSettings : UpdatedSettings;
            Assert.That(await OpenDatabase(options, false).GetEconomyMarketSettings(),
                Is.EqualTo((expectedRevision + 1, expectedSettings)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task QuoteResetDoesNotEraseSettingsAndSettingsDoNotEraseQuotes()
    {
        await _database.UpsertEconomyMarketQuotes([("proto:persistent", 2.0, 0f)]);
        Assert.That(await _database.TrySaveEconomyMarketSettings(0, InitialSettings), Is.True);
        Assert.That(await _database.GetAllEconomyMarketQuotes(), Has.Count.EqualTo(1));

        await _database.ClearEconomyMarketQuotes();
        Assert.That(await _database.GetEconomyMarketSettings(), Is.EqualTo((1L, InitialSettings)));
    }

    [TestCase(-1L)]
    [TestCase(long.MaxValue)]
    public void InvalidRevisionIsRejected(long revision)
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _database.TrySaveEconomyMarketSettings(revision, InitialSettings));
    }

    [TestCase("")]
    [TestCase(" ")]
    public void EmptySettingsAreRejected(string settings)
    {
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _database.TrySaveEconomyMarketSettings(0, settings));
    }

    [Test]
    public void NullSettingsAreRejected()
    {
        Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await _database.TrySaveEconomyMarketSettings(0, null));
    }

    [Test]
    public async Task FailedWritePropagatesWithoutChangingRevision()
    {
        Assert.That(await _database.TrySaveEconomyMarketSettings(0, InitialSettings), Is.True);
        await using (var context = new SqliteServerDbContext(_options))
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_settings_update BEFORE UPDATE ON economy_market_settings
                BEGIN SELECT RAISE(ABORT, 'Injected write failure'); END;
                """);
        }

        Assert.ThrowsAsync<SqliteException>(async () =>
            await _database.TrySaveEconomyMarketSettings(1, UpdatedSettings));
        Assert.That(await _database.GetEconomyMarketSettings(), Is.EqualTo((1L, InitialSettings)));
    }

    [Test]
    public void DatabaseRejectsAdditionalSettingsRows()
    {
        using var context = new SqliteServerDbContext(_options);
        var exception = Assert.ThrowsAsync<SqliteException>(async () => await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO economy_market_settings (id, revision, settings) VALUES (2, 1, {InitialSettings});
            """));
        Assert.That(exception.SqliteErrorCode, Is.EqualTo(19), "The singleton constraint must reject the row.");
    }

    [Test]
    public async Task SettingsMigrationPreservesExistingQuotes()
    {
        await _database.UpsertEconomyMarketQuotes([("proto:before-upgrade", 2.0, 0f)]);
        await using (var context = new SqliteServerDbContext(_options))
        {
            await context.Database.ExecuteSqlRawAsync("DROP TABLE economy_market_settings;");
            await context.Database.ExecuteSqlRawAsync("""
                DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%_EconomyMarketSettings';
                """);
        }

        var reopened = OpenDatabase();
        Assert.That(await reopened.GetEconomyMarketSettings(), Is.Null);
        Assert.That(await reopened.GetAllEconomyMarketQuotes(), Has.Count.EqualTo(1));
        Assert.That(await reopened.TrySaveEconomyMarketSettings(0, InitialSettings), Is.True);
        Assert.That(await reopened.GetEconomyMarketSettings(), Is.EqualTo((1L, InitialSettings)));
    }

    [Test]
    public void BothModelsMatchTheirMigrationSnapshots()
    {
        using var sqlite = new SqliteServerDbContext(_options);
        using var postgres = new PostgresServerDbContext(new DbContextOptionsBuilder<PostgresServerDbContext>()
            .UseNpgsql("Host=localhost;Database=market_settings_model_check").Options);
        Assert.Multiple(() =>
        {
            Assert.That(sqlite.Database.HasPendingModelChanges(), Is.False);
            Assert.That(postgres.Database.HasPendingModelChanges(), Is.False);
        });
    }

    private ServerDbSqlite OpenDatabase(DbContextOptions<SqliteServerDbContext> options = null, bool inMemory = true)
    {
        return new ServerDbSqlite(() => options ?? _options, inMemory, _configuration, true,
            _logs.GetSawmill("market-settings-test"));
    }
}
