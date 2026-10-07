// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Exodus.Economy;
using Content.Server.Database;
using Content.Shared._Exodus.Economy;
using Content.Shared._Exodus.Economy.Admin;
using Moq;
using NUnit.Framework;
using Robust.Shared.Asynchronous;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Tests.Server._Exodus.Economy;

[TestFixture]
public sealed class MarketSettingsPersistenceTest
{
    private static readonly ProtoId<MarketCommodityGroupPrototype> GeneralGroup = "TestGeneral";
    private static readonly ProtoId<MarketCommodityGroupPrototype> GasGroup = "TestGas";
    private static readonly ProtoId<MarketCommodityGroupPrototype> RemovedGroup = "RemovedGroup";

    private MarketSettingsSystem _settings;
    private Mock<IServerDbManager> _database;
    private Mock<IPrototypeManager> _prototypes;
    private Mock<ITaskManager> _tasks;
    private ConcurrentQueue<Action> _callbacks;
    private SemaphoreSlim _callbackAvailable;

    [SetUp]
    public void SetUp()
    {
        _database = new Mock<IServerDbManager>(MockBehavior.Strict);
        _database.Setup(d => d.GetEconomyMarketSettings(It.IsAny<CancellationToken>()))
            .ReturnsAsync(((long, string)?) null);
        _prototypes = new Mock<IPrototypeManager>();
        _prototypes.Setup(p => p.EnumeratePrototypes<MarketCommodityGroupPrototype>())
            .Returns([Group(GeneralGroup, 1f), Group(GasGroup, 0.003f)]);
        _prototypes.Setup(p => p.HasIndex(It.IsAny<ProtoId<MarketCommodityGroupPrototype>>()))
            .Returns((ProtoId<MarketCommodityGroupPrototype> id) => id == GeneralGroup || id == GasGroup);
        _callbacks = new ConcurrentQueue<Action>();
        _callbackAvailable = new SemaphoreSlim(0);
        _tasks = new Mock<ITaskManager>();
        _tasks.Setup(t => t.RunOnMainThread(It.IsAny<Action>())).Callback<Action>(callback =>
        {
            _callbacks.Enqueue(callback);
            _callbackAvailable.Release();
        });
        _settings = CreateSystem();
    }

    [TearDown]
    public void TearDown()
    {
        _callbackAvailable.Dispose();
    }

    [Test]
    public async Task ApplyPublishesOnlyAfterSaveSucceeds()
    {
        await Load();
        var before = _settings.GetSnapshot();
        var write = new TaskCompletionSource<bool>();
        _database.Setup(d => d.TrySaveEconomyMarketSettings(0, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(write.Task);
        var groups = new Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> { [GasGroup] = 0.0006 };
        bool? result = null;

        _settings.Apply(before.Revision, new(ImpactStrength: 0.12), groups, (success, _) => result = success);
        groups[GasGroup] = 9;

        Assert.Multiple(() =>
        {
            Assert.That(_settings.Saving, Is.True);
            Assert.That(_settings.Current, Is.EqualTo(before.Global));
            Assert.That(_settings.GetSnapshot().Revision, Is.EqualTo(before.Revision));
            Assert.That(_settings.GetImpactStrength(GasGroup), Is.EqualTo(0.00024).Within(1e-10));
            Assert.That(result, Is.Null);
        });

        write.SetResult(true);
        await DispatchCallback();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.True);
            Assert.That(_settings.Saving, Is.False);
            Assert.That(_settings.Current.ImpactStrength, Is.EqualTo(0.12));
            Assert.That(_settings.GetImpactStrength(GasGroup), Is.EqualTo(0.0006));
            Assert.That(_settings.GetSnapshot().Revision, Is.EqualTo(before.Revision + 1));
        });
    }

    [Test]
    public async Task FailedSavePreservesEffectiveSettingsAndRevision()
    {
        await Load();
        var before = _settings.GetSnapshot();
        _database.Setup(d => d.TrySaveEconomyMarketSettings(0, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Injected settings write failure"));
        bool? result = null;
        string error = null;

        _settings.Apply(before.Revision, new(Enabled: false, ImpactStrength: 0.4),
            new() { [GasGroup] = 0 }, (success, reason) => (result, error) = (success, reason));
        await DispatchCallback();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo("economy-admin-error-save"));
            Assert.That(_settings.Ready, Is.True);
            Assert.That(_settings.Saving, Is.False);
            Assert.That(_settings.Current, Is.EqualTo(before.Global));
            Assert.That(_settings.GetSnapshot().Revision, Is.EqualTo(before.Revision));
            Assert.That(_settings.GetSnapshot().GroupOverrides, Is.Empty);
            Assert.That(_settings.GetImpactStrength(GasGroup), Is.EqualTo(0.00024).Within(1e-10));
        });
    }

    [Test]
    public async Task StaleSnapshotCannotSubmitADatabaseWrite()
    {
        await Load();
        var before = _settings.GetSnapshot();
        bool? result = null;
        string error = null;

        _settings.Apply(before.Revision - 1, new(Enabled: false), new(),
            (success, reason) => (result, error) = (success, reason));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(error, Is.EqualTo("economy-admin-error-conflict"));
            Assert.That(_settings.Current, Is.EqualTo(before.Global));
            Assert.That(_settings.GetSnapshot().Revision, Is.EqualTo(before.Revision));
        });
        _database.Verify(d => d.TrySaveEconomyMarketSettings(It.IsAny<long>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task SavedDocumentRestoresOverridesAndUsesPersistedRevisionForNextSave()
    {
        await Load();
        string document = null;
        _database.Setup(d => d.TrySaveEconomyMarketSettings(0, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<long, string, CancellationToken>((_, json, _) => document = json)
            .ReturnsAsync(true);
        _settings.Apply(_settings.GetSnapshot().Revision,
            new(Enabled: false, ImpactStrength: 0.2, ReferenceVolume: 250, PurchaseMargin: 0.1),
            new() { [GasGroup] = 0.0008 }, (_, _) => { });
        await DispatchCallback();
        Assert.That(document, Is.Not.Null);

        _database.Setup(d => d.GetEconomyMarketSettings(It.IsAny<CancellationToken>()))
            .ReturnsAsync((8L, document));
        var restarted = CreateSystem();
        restarted.RefreshLoad();
        await DispatchCallback();

        var restored = restarted.GetSnapshot();
        Assert.Multiple(() =>
        {
            Assert.That(restarted.Ready, Is.True);
            Assert.That(restored.Global.Enabled, Is.False);
            Assert.That(restored.Global.ImpactStrength, Is.EqualTo(0.2));
            Assert.That(restored.Global.ReferenceVolume, Is.EqualTo(250));
            Assert.That(restored.Global.PurchaseMargin, Is.EqualTo(0.1));
            Assert.That(restored.Overrides.DecayRate, Is.Null);
            Assert.That(restarted.GetImpactStrength(GeneralGroup), Is.EqualTo(0.2));
            Assert.That(restarted.GetImpactStrength(GasGroup), Is.EqualTo(0.0008));
        });
        restored.GroupOverrides[GasGroup] = 5;
        Assert.That(restarted.GetSnapshot().GroupOverrides[GasGroup], Is.EqualTo(0.0008));

        _database.Setup(d => d.TrySaveEconomyMarketSettings(8, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        bool? result = null;
        restarted.Apply(restored.Revision, new(), new(), (success, _) => result = success);
        await DispatchCallback();
        Assert.That(result, Is.True);
        _database.Verify(d => d.TrySaveEconomyMarketSettings(8, It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task UnknownDocumentVersionKeepsSettingsUnavailable()
    {
        _database.Setup(d => d.GetEconomyMarketSettings(It.IsAny<CancellationToken>()))
            .ReturnsAsync((3L, """{"Version":2,"Global":{},"Groups":{}}"""));
        var before = _settings.GetSnapshot();
        _settings.RefreshLoad();
        await DispatchCallback();

        Assert.Multiple(() =>
        {
            Assert.That(_settings.Ready, Is.False);
            Assert.That(_settings.LastError, Is.EqualTo("economy-admin-error-load"));
            Assert.That(_settings.Current, Is.EqualTo(before.Global));
            Assert.That(_settings.GetSnapshot().Revision, Is.EqualTo(before.Revision));
        });
    }

    [Test]
    public async Task RemovedGroupOverrideIsRetainedWithoutAffectingActiveGroups()
    {
        _database.Setup(d => d.GetEconomyMarketSettings(It.IsAny<CancellationToken>()))
            .ReturnsAsync((3L,
                """{"Version":1,"Global":{"ImpactStrength":0.2},"Groups":{"RemovedGroup":4.0}}"""));
        await Load();

        Assert.Multiple(() =>
        {
            Assert.That(_settings.GetSnapshot().GroupOverrides[RemovedGroup], Is.EqualTo(4));
            Assert.That(_settings.GetImpactStrength(RemovedGroup), Is.EqualTo(0.2));
            Assert.That(_settings.GetImpactStrength(GeneralGroup), Is.EqualTo(0.2));
            Assert.That(_settings.GetImpactStrength(GasGroup), Is.EqualTo(0.0006).Within(1e-10));
        });
    }

    private async Task Load()
    {
        _settings.RefreshLoad();
        await DispatchCallback();
        Assert.That(_settings.Ready, Is.True);
    }

    private async Task DispatchCallback()
    {
        Assert.That(await _callbackAvailable.WaitAsync(TimeSpan.FromSeconds(10)), Is.True,
            "The persistence operation must dispatch its result to the main thread.");
        Assert.That(_callbacks.TryDequeue(out var callback), Is.True);
        callback();
    }

    private MarketSettingsSystem CreateSystem()
    {
        var system = new MarketSettingsSystem();
        SetField(system, "_database", _database.Object);
        SetField(system, "_prototypes", _prototypes.Object);
        SetField(system, "_tasks", _tasks.Object);
        SetField(system, "_timing", new Mock<IGameTiming>().Object);
        typeof(EntitySystem).GetProperty(nameof(EntitySystem.Log))!.SetValue(system, new Mock<ISawmill>().Object);
        return system;
    }

    private static void SetField(MarketSettingsSystem system, string name, object value)
    {
        typeof(MarketSettingsSystem).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(system, value);
    }

    private static MarketCommodityGroupPrototype Group(ProtoId<MarketCommodityGroupPrototype> id, float multiplier)
    {
        var group = new MarketCommodityGroupPrototype();
        typeof(MarketCommodityGroupPrototype).GetProperty(nameof(MarketCommodityGroupPrototype.ID))!.SetValue(group, id.Id);
        typeof(MarketCommodityGroupPrototype).GetProperty(nameof(MarketCommodityGroupPrototype.ImpactMultiplier))!
            .SetValue(group, multiplier);
        return group;
    }
}
