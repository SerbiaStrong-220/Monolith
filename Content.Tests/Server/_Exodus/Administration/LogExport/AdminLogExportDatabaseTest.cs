// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Exodus.Administration.LogExport;
using Content.Server.Database;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using NUnit.Framework;
using Robust.Shared.Log;
using Robust.Shared.Timing;
using Robust.UnitTesting;

namespace Content.Tests.Server._Exodus.Administration.LogExport;

[TestFixture]
public sealed class AdminLogExportDatabaseTest
{
    private SqliteConnection _connection;
    private DbContextOptions<SqliteServerDbContext> _options;
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
        var configuration = MockInterfaces.MakeConfigurationManager(new Mock<IGameTiming>().Object, _logs,
            loadCvarsFromTypes: [typeof(CCVars)]);
        configuration.SetCVar(CCVars.DatabaseSqliteDelay, 0);
        _database = new ServerDbSqlite(() => _options, true, configuration, true, _logs.GetSawmill("log-export-test"));
        await using var context = new SqliteServerDbContext(_options);
        context.Server.Add(new Content.Server.Database.Server { Id = 1, Name = "Test" });
        context.Round.AddRange(new Round { Id = 7, ServerId = 1 }, new Round { Id = 8, ServerId = 1 });
        await context.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
        _logs.Dispose();
    }

    [Test]
    public async Task KeysetPagesPreserveGapsAndEqualDatesWithoutIncludingLaterLogs()
    {
        await AddLogs(7, (9, "nine"), (1, "one"), (8, "eight"), (3, "three"));
        await AddLogs(8, (2, "other round"));
        var snapshot = await _database.GetAdminLogExportSnapshot(7);
        Assert.That(snapshot, Is.Not.Null);
        Assert.That(snapshot.Count, Is.EqualTo(4));
        Assert.That(snapshot.LastLogId, Is.EqualTo(9));
        await AddLogs(7, (10, "after snapshot"));

        var first = await _database.GetAdminLogExportPage(7, 0, snapshot.LastLogId, 2);
        var second = await _database.GetAdminLogExportPage(7, first[^1].Id, snapshot.LastLogId, 2);
        var end = await _database.GetAdminLogExportPage(7, second[^1].Id, snapshot.LastLogId, 2);
        Assert.Multiple(() =>
        {
            Assert.That(first.Select(log => log.Id), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(second.Select(log => log.Id), Is.EqualTo(new[] { 8, 9 }));
            Assert.That(first[0].Message, Is.EqualTo("one"));
            Assert.That(first[0].Date.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(end, Is.Empty);
        });
    }

    [Test]
    public async Task EmptyRoundIsDistinctFromMissingRound()
    {
        var empty = await _database.GetAdminLogExportSnapshot(7);
        Assert.That(empty, Is.Not.Null);
        Assert.That(empty.Count, Is.Zero);
        Assert.That(empty.LastLogId, Is.Zero);
        Assert.That(await _database.GetAdminLogExportSnapshot(999), Is.Null);
        Assert.That(await _database.GetAdminLogExportSnapshot(-1), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OversizedMessageFailsWithoutReturningATruncatedRecord(bool multibyte)
    {
        var message = multibyte ? new string('я', 600_000) : new string('x', 1_048_577);
        await AddLogs(7, (1, message));
        var error = Assert.ThrowsAsync<AdminLogExportDataException>(async () =>
            await _database.GetAdminLogExportPage(7, 0, 1, 256));
        Assert.That(error.ErrorKey, Is.EqualTo("admin-logs-export-error-record-too-large"));
    }

    [Test]
    public async Task PageCharacterBudgetDoesNotSkipRemainingRecords()
    {
        await AddLogs(7, (1, new string('x', 700_000)), (2, new string('y', 700_000)));
        var first = await _database.GetAdminLogExportPage(7, 0, 2, 256);
        var second = await _database.GetAdminLogExportPage(7, first[^1].Id, 2, 256);
        Assert.That(first.Select(log => log.Id), Is.EqualTo(new[] { 1 }));
        Assert.That(second.Select(log => log.Id), Is.EqualTo(new[] { 2 }));
    }

    [TestCase(2)]
    [TestCase(1025)]
    public async Task PlayerIdsAreExportedOnlyWithinTheRecordLimit(int count)
    {
        await AddLogs(7, (1, "players"));
        var ids = await AddPlayers(7, 1, count);

        if (count > 1024)
        {
            Assert.ThrowsAsync<AdminLogExportDataException>(async () =>
                await _database.GetAdminLogExportPage(7, 0, 1, 128));
            return;
        }

        var page = await _database.GetAdminLogExportPage(7, 0, 1, 128);
        Assert.That(page.Single().Players, Is.EquivalentTo(ids));
    }

    [Test]
    public async Task LargeMessageIsSelectedSeparatelyFromItsManyPlayers()
    {
        var message = new string('x', 256 * 1024);
        await AddLogs(7, (1, message));
        var players = await AddPlayers(7, 1, 8);
        var capture = new ExportQueryCapture();
        _options = new DbContextOptionsBuilder<SqliteServerDbContext>(_options).AddInterceptors(capture).Options;

        var page = await _database.GetAdminLogExportPage(7, 0, 1, 128);
        Assert.That(page.Single().Message, Is.EqualTo(message));
        Assert.That(page.Single().Players, Is.EquivalentTo(players));

        // Inspect the executed reader's columns, not just the LINQ expression. A collection join
        // repeats a large message for every player even though EF returns only one log object.
        var messageQuery = capture.Queries.Single(query => query.Columns.Contains("message", StringComparer.OrdinalIgnoreCase));
        Assert.That(messageQuery.Sql, Does.Not.Contain("JOIN").IgnoreCase,
            "Export message bytes must not be multiplied by the number of related players.");
        Assert.That(messageQuery.Columns, Does.Not.Contain("player_user_id"));
        Assert.That(capture.Queries.Any(query => query.Columns.Contains("player_user_id") &&
            !query.Columns.Contains("message")), Is.True, "Players must be read without the message column.");
    }

    [Test]
    public void InvalidLimitAndCancellationAreRejected()
    {
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _database.GetAdminLogExportPage(7, 0, 10, 257));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await _database.GetAdminLogExportPage(7, 0, 10, 0));
        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await _database.GetAdminLogExportSnapshot(7, new CancellationToken(true)));
    }

    private async Task<Guid[]> AddPlayers(int roundId, int logId, int count)
    {
        var ids = new Guid[count];
        await using var context = new SqliteServerDbContext(_options);
        for (var i = 0; i < count; i++)
        {
            ids[i] = Guid.NewGuid();
            context.Player.Add(new Player
            {
                UserId = ids[i],
                FirstSeenTime = DateTime.UtcNow,
                LastSeenTime = DateTime.UtcNow,
                LastSeenUserName = "Test",
                LastSeenAddress = IPAddress.Loopback,
            });
            context.AdminLogPlayer.Add(new AdminLogPlayer { RoundId = roundId, LogId = logId, PlayerUserId = ids[i] });
        }
        await context.SaveChangesAsync();
        return ids;
    }

    private async Task AddLogs(int roundId, params (int Id, string Message)[] logs)
    {
        await using var context = new SqliteServerDbContext(_options);
        using var json = JsonDocument.Parse("{}");
        foreach (var log in logs)
        {
            context.AdminLog.Add(new AdminLog
            {
                RoundId = roundId,
                Id = log.Id,
                Date = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc),
                Type = LogType.Unknown,
                Impact = LogImpact.Low,
                Message = log.Message,
                Json = json,
                Players = [],
            });
        }
        await context.SaveChangesAsync();
    }

    private sealed class ExportQueryCapture : DbCommandInterceptor
    {
        public readonly List<(string Sql, string[] Columns)> Queries = [];

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            var columns = new string[result.FieldCount];
            for (var index = 0; index < columns.Length; index++)
                columns[index] = result.GetName(index);
            Queries.Add((command.CommandText, columns));
            return ValueTask.FromResult(result);
        }
    }
}
