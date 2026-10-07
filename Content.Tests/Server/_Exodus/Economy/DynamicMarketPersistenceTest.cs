// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Exodus.Economy;
using Content.Server.Database;
using Moq;
using NUnit.Framework;
using Robust.Shared.Asynchronous;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Timing;

namespace Content.Tests.Server._Exodus.Economy;

[TestFixture]
public sealed class DynamicMarketPersistenceTest
{
    private DynamicMarketSystem _market;
    private Mock<IServerDbManager> _database;
    private Mock<ITaskManager> _tasks;
    private Queue<Action> _callbacks;
    private TimeSpan _now;

    [SetUp]
    public void SetUp()
    {
        _market = new DynamicMarketSystem();
        var settings = new MarketSettingsSystem();
        typeof(MarketSettingsSystem).GetProperty(nameof(MarketSettingsSystem.Ready))!.SetValue(settings, true);
        SetField("_settings", settings);
        _database = new Mock<IServerDbManager>();
        _callbacks = new Queue<Action>();
        _now = TimeSpan.Zero;
        var timing = new Mock<IGameTiming>();
        timing.SetupGet(t => t.CurTime).Returns(() => _now);
        _tasks = new Mock<ITaskManager>();
        _tasks.Setup(t => t.RunOnMainThread(It.IsAny<Action>()))
            .Callback<Action>(_callbacks.Enqueue);
        SetField("_db", _database.Object);
        SetField("_timing", timing.Object);
        SetField("_taskManager", _tasks.Object);
        typeof(EntitySystem).GetProperty(nameof(EntitySystem.Log))!.SetValue(_market, new Mock<ISawmill>().Object);
    }

    [Test]
    public void ResetKeyDuringInitialLoadCannotBeUndoneByFlush()
    {
        SetField("_loadStarted", true);
        _market.ResetKey("proto:reset");
        Invoke("StartFlush", true);
        Invoke("ApplyLoadedRows", new List<(string, double, float, DateTime)>
        {
            ("proto:reset", 2.0, 0f, DateTime.UtcNow),
            ("proto:untouched", 3.0, 0f, DateTime.UtcNow),
        });

        Assert.Multiple(() =>
        {
            Assert.That(_market.GetFactor("proto:reset"), Is.EqualTo(1.0));
            Assert.That(_market.GetFactor("proto:untouched"), Is.EqualTo(3.0));
        });
    }

    [Test]
    public void FailedLoadRetriesAndPreservesTrades()
    {
        IReadOnlyList<(string, double, float, DateTime)> rows = new List<(string, double, float, DateTime)>
        {
            ("proto:traded", 2.0, 0f, DateTime.UtcNow),
            ("proto:untouched", 3.0, 0f, DateTime.UtcNow),
        };
        _database.Setup(d => d.GetAllEconomyMarketQuotes(It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        _market.SetFactor("proto:traded", 4.0);
        Invoke("FinishLoadFailure", "Test database outage");
        _now = TimeSpan.FromMinutes(2);
        Invoke("UpdatePersistence");
        while (_callbacks.TryDequeue(out var callback))
            callback();

        Assert.Multiple(() =>
        {
            Assert.That(_market.GetFactor("proto:traded"), Is.EqualTo(4.0));
            Assert.That(_market.GetFactor("proto:untouched"), Is.EqualTo(3.0));
        });
    }

    [Test]
    public void FailedFullClearWaitsBeforeRetrying()
    {
        SetField("_loadCompleted", true);
        Invoke("FinishFlush", true, new List<(string, double, float)>(), new List<string>(), "Test database outage");

        // A failed write must not immediately submit another operation, including from the next tick.
        Invoke("UpdatePersistence");
        Assert.That(_database.Invocations, Is.Empty);
    }

    [Test]
    public void ShutdownWaitsForInFlightWriteAndRetainsPendingDeletion()
    {
        var stored = new Dictionary<string, double> { ["proto:deleted"] = 2.0 };
        var pending = new TaskCompletionSource();
        var firstWrite = true;
        _database.Setup(d => d.SaveEconomyMarketQuotes(
                It.IsAny<IReadOnlyList<(string, double, float)>>(), It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyList<(string, double, float)> upserts, IReadOnlyCollection<string> deletes,
                bool clear, CancellationToken _) =>
            {
                if (firstWrite)
                {
                    firstWrite = false;
                    return pending.Task;
                }

                if (clear)
                    stored.Clear();
                foreach (var (key, factor, _) in upserts)
                    stored[key] = factor;
                foreach (var key in deletes)
                    stored.Remove(key);
                return Task.CompletedTask;
            });
        _tasks.Setup(t => t.BlockWaitOnTask(It.IsAny<Task>())).Callback<Task>(task =>
        {
            pending.SetResult();
            task.GetAwaiter().GetResult();
        });
        SetField("_loadCompleted", true);
        _market.ResetKey("proto:deleted");
        Invoke("StartFlush", false);
        _market.SetFactor("proto:current", 4.0);

        _market.FlushForShutdown();

        Assert.Multiple(() =>
        {
            Assert.That(stored.ContainsKey("proto:deleted"), Is.False);
            Assert.That(stored["proto:current"], Is.EqualTo(4.0));
        });
    }

    [Test]
    public void AdministrativeResetPublishesOnlyAfterSuccessfulDatabaseWrite()
    {
        SetField("_loadCompleted", true);
        _market.SetFactor("proto:before", 2);
        var write = new TaskCompletionSource();
        _database.Setup(d => d.SaveEconomyMarketQuotes(It.IsAny<IReadOnlyList<(string, double, float)>>(),
            It.IsAny<IReadOnlyCollection<string>>(), true, It.IsAny<CancellationToken>())).Returns(write.Task);
        bool? result = null;
        _market.ApplyAdminQuotes(null, null, true, (success, _) => result = success);
        Assert.Multiple(() =>
        {
            Assert.That(_market.AdminQuotesBusy, Is.True);
            Assert.That(_market.GetFactor("proto:before"), Is.EqualTo(2));
            Assert.That(result, Is.Null);
        });
        Invoke("StartFlush", true);
        Assert.That(_database.Invocations, Has.Count.EqualTo(1));
        write.SetResult();
        while (_callbacks.TryDequeue(out var callback))
            callback();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(_market.AdminQuotesBusy, Is.False);
            Assert.That(_market.GetAllQuotes(), Is.Empty);
        });
    }

    [Test]
    public void FailedAdministrativeResetPreservesQuotesAndReportsFailure()
    {
        SetField("_loadCompleted", true);
        _market.SetFactor("proto:before", 3);
        _database.Setup(d => d.SaveEconomyMarketQuotes(It.IsAny<IReadOnlyList<(string, double, float)>>(),
            It.IsAny<IReadOnlyCollection<string>>(), true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected reset failure"));
        bool? result = null;
        _market.ApplyAdminQuotes(null, null, true, (success, _) => result = success);
        while (_callbacks.TryDequeue(out var callback))
            callback();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(_market.AdminQuotesBusy, Is.False);
            Assert.That(_market.GetFactor("proto:before"), Is.EqualTo(3));
        });
    }

    private void SetField(string name, object value)
    {
        typeof(DynamicMarketSystem).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_market, value);
    }

    private object Invoke(string name, params object[] args)
    {
        return typeof(DynamicMarketSystem).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_market, args);
    }
}
