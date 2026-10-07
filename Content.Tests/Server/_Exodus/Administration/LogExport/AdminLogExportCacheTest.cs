// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Exodus.Administration.LogExport;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Moq;
using NUnit.Framework;
using Robust.Shared.Asynchronous;

namespace Content.Tests.Server._Exodus.Administration.LogExport;

[TestFixture]
public sealed class AdminLogExportCacheTest
{
    private AdminLogManager _manager;
    private Mock<IServerDbManager> _database;
    private Queue<Action> _callbacks;

    [SetUp]
    public void SetUp()
    {
        _manager = new AdminLogManager();
        _database = new Mock<IServerDbManager>(MockBehavior.Strict);
        _callbacks = new Queue<Action>();
        var tasks = new Mock<ITaskManager>();
        tasks.Setup(t => t.RunOnMainThread(It.IsAny<Action>())).Callback<Action>(_callbacks.Enqueue);
        SetField("_db", _database.Object);
        SetField("_exportTasks", tasks.Object);
        _manager.RoundStarting(7);
    }

    [Test]
    public async Task CachedSnapshotKeepsInsertionOrderAndExcludesAppendedLogs()
    {
        AddCached(5, "five");
        AddCached(7, "seven");
        AddCached(2, "preround");
        var pending = _manager.CreateExportSnapshotAsync(7, default);
        Assert.That(pending.IsCompleted, Is.False, "Cache access must be dispatched to the main thread.");
        var snapshot = await Dispatch(pending);
        AddCached(10, "new log");

        var first = await Dispatch(_manager.ReadExportPageAsync(snapshot, 0, 0, 2, default));
        var second = await Dispatch(_manager.ReadExportPageAsync(snapshot, 2, 7, 2, default));
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Count, Is.EqualTo(3));
            Assert.That(first.Select(log => log.Id), Is.EqualTo(new[] { 5, 7 }));
            Assert.That(second.Select(log => log.Id), Is.EqualTo(new[] { 2 }));
            Assert.That(_database.Invocations, Is.Empty);
        });
    }

    [Test]
    public async Task EvictedAndReusedCacheFailsWithoutFallingBackToIncompleteDatabase()
    {
        AddCached(1, "old round");
        var snapshot = await Dispatch(_manager.CreateExportSnapshotAsync(7, default));
        _manager.RoundStarting(8);
        _manager.RoundStarting(9);
        _manager.RoundStarting(10);
        AddCached(1, "new round in reused list");

        var error = Assert.ThrowsAsync<AdminLogExportDataException>(async () =>
            await Dispatch(_manager.ReadExportPageAsync(snapshot, 0, 0, 128, default)));
        Assert.That(error.ErrorKey, Is.EqualTo("admin-logs-export-error-cache-expired"));
        Assert.That(_database.Invocations, Is.Empty);
    }

    [Test]
    public async Task UncachedRoundUsesTheDatabaseSnapshotAndFrozenUpperBound()
    {
        var saved = new AdminLogExportSnapshot(1, 2, 90, DateTime.UtcNow);
        _database.Setup(d => d.GetAdminLogExportSnapshot(1, It.IsAny<CancellationToken>())).ReturnsAsync(saved);
        _database.Setup(d => d.GetAdminLogExportPage(1, 40, 90, 128, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SharedAdminLog(90, LogType.Unknown, LogImpact.Low, DateTime.UtcNow, "last", []) });
        var snapshot = await Dispatch(_manager.CreateExportSnapshotAsync(1, default));
        var page = await _manager.ReadExportPageAsync(snapshot, 1, 40, 128, default);
        Assert.That(page.Single().Id, Is.EqualTo(90));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OversizedCacheRecordFailsExplicitly(bool manyPlayers)
    {
        AddCached(1, manyPlayers ? "players" : new string('я', 600_000), manyPlayers ? new Guid[1025] : []);
        var snapshot = await Dispatch(_manager.CreateExportSnapshotAsync(7, default));
        var error = Assert.ThrowsAsync<AdminLogExportDataException>(async () =>
            await Dispatch(_manager.ReadExportPageAsync(snapshot, 0, 0, 128, default)));
        Assert.That(error.ErrorKey, Is.EqualTo("admin-logs-export-error-record-too-large"));
    }

    private async Task<T> Dispatch<T>(Task<T> task)
    {
        Assert.That(_callbacks.TryDequeue(out var callback), Is.True);
        callback();
        return await task;
    }

    private void AddCached(int id, string message, Guid[] players = null)
    {
        var log = new SharedAdminLog(id, LogType.Unknown, LogImpact.Low, DateTime.UtcNow, message, players ?? []);
        typeof(AdminLogManager).GetMethod("CacheLog", BindingFlags.Instance | BindingFlags.NonPublic,
            null, [typeof(SharedAdminLog)], null)!.Invoke(_manager, [log]);
    }

    private void SetField(string name, object value)
    {
        typeof(AdminLogManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_manager, value);
    }
}
