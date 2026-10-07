// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Shared._Exodus.CCVar;
using Content.Shared._NF.Market;
using Content.Shared.GameTicking;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketInventoryTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusInventoryFirst

- type: entity
  id: ExodusInventorySecond

- type: stack
  id: ExodusInventoryStack
  name: stack-steel
  spawn: ExodusInventoryStackItem
  maxCount: 50

- type: entity
  id: ExodusInventoryStackItem
  components:
  - type: Stack
    stackType: ExodusInventoryStack
    count: 1
";

    [Test]
    public async Task InvalidStockChangesAndOverflowLeaveInventoryUnchanged()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var inventory = server.System<MarketInventorySystem>();
            inventory.Clear();
            Assert.Multiple(() =>
            {
                Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 0, 100), Is.False);
                Assert.That(inventory.TryAddStock("ExodusInventoryFirst", -1, 100), Is.False);
                Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 1, -100), Is.False);
                Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 1, double.NaN), Is.False);
                Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 1, double.PositiveInfinity), Is.False);
                Assert.That(inventory.TryAddStock("ExodusInventoryUnknown", 1, 100), Is.False);
                Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 1, 100, "ExodusInventoryStack"), Is.False);
                Assert.That(inventory.TryAddStock("ExodusInventoryStackItem", 1, 100), Is.False);
                Assert.That(inventory.TryAddStock("ExodusInventoryStackItem", 1, 100, "ExodusInventoryUnknownStack"), Is.False);
                Assert.That(inventory.TryAddStock("ExodusInventoryStackItem", 1, 100, "Steel"), Is.False);
                Assert.That(inventory.GetStock(), Is.Empty);
            });

            Assert.That(inventory.TryAddStock("ExodusInventoryFirst", int.MaxValue, 100), Is.True);
            Assert.That(inventory.TryAddStock("ExodusInventoryStackItem", 1, 200, "ExodusInventoryStack"), Is.True);
            Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 1, 999), Is.False);
            Assert.That(inventory.TryTakeStock("ExodusInventoryFirst", 0, out _), Is.False);
            Assert.That(inventory.TryTakeStock("ExodusInventoryFirst", -1, out _), Is.False);
            Assert.That(inventory.TryGetStock("ExodusInventoryFirst", out var entry), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(entry!.Quantity, Is.EqualTo(int.MaxValue));
                Assert.That(entry.Price, Is.EqualTo(100));
                Assert.That(inventory.GetStock(), Has.Count.EqualTo(2));
            });
            inventory.Clear();
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnavailableCartDoesNotPartiallyConsumeStock(bool duplicateLines)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var inventory = server.System<MarketInventorySystem>();
            inventory.Clear();
            Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 1, 100), Is.True);
            Assert.That(inventory.TryAddStock("ExodusInventorySecond", 1, 200), Is.True);
            MarketData[] request = duplicateLines
                ? [new("ExodusInventoryFirst", null, 1, 100), new("ExodusInventoryFirst", null, 1, 100)]
                : [new("ExodusInventoryFirst", null, 1, 100), new("ExodusInventorySecond", null, 2, 200)];

            Assert.That(inventory.TryTakeStock(request, out var taken), Is.False);
            Assert.That(taken, Is.Null);
            Assert.That(inventory.TryGetStock("ExodusInventoryFirst", out var first), Is.True);
            Assert.That(inventory.TryGetStock("ExodusInventorySecond", out var second), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(first!.Quantity, Is.EqualTo(1));
                Assert.That(first.Price, Is.EqualTo(100));
                Assert.That(second!.Quantity, Is.EqualTo(1));
                Assert.That(second.Price, Is.EqualTo(200));
            });
            inventory.Clear();
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AvailableCartConsumesEveryLineWithItsStoredPrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var inventory = server.System<MarketInventorySystem>();
            inventory.Clear();
            Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 2, 0.25), Is.True);
            Assert.That(inventory.TryAddStock("ExodusInventorySecond", 1, 200), Is.True);
            MarketData[] request = [new("ExodusInventoryFirst", null, 1, 0.25), new("ExodusInventorySecond", null, 1, 200)];

            Assert.That(inventory.TryTakeStock(request, out var taken), Is.True);
            Assert.That(taken, Has.Count.EqualTo(2));
            Assert.That(inventory.TryGetStock("ExodusInventoryFirst", out var remaining), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(remaining!.Quantity, Is.EqualTo(1));
                Assert.That(remaining.Price, Is.EqualTo(0.25));
                Assert.That(inventory.TryGetStock("ExodusInventorySecond", out _), Is.False);
                Assert.That(taken![0].Quantity, Is.EqualTo(1));
                Assert.That(taken[0].Price, Is.EqualTo(0.25));
                Assert.That(taken[1].Quantity, Is.EqualTo(1));
                Assert.That(taken[1].Price, Is.EqualTo(200));
            });
            inventory.Clear();
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StockSnapshotsCannotMutateTheInventory()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var inventory = server.System<MarketInventorySystem>();
            inventory.Clear();
            Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 2, 100), Is.True);
            var snapshot = inventory.GetStock();
            snapshot[0].Quantity = int.MaxValue;
            snapshot[0].Price = 0;
            Assert.That(inventory.TryGetStock("ExodusInventoryFirst", out var entry), Is.True);
            entry!.Quantity = 0;

            Assert.That(inventory.TryGetStock("ExodusInventoryFirst", out var actual), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(actual!.Quantity, Is.EqualTo(2));
                Assert.That(actual.Price, Is.EqualTo(100));
            });
            inventory.Clear();
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RoundCleanupClearsStockAndPreservesQuotesAndSettings()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var configuration = server.ResolveDependency<IConfigurationManager>();
            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            configuration.SetCVar(EXCVars.DynamicMarketBuyImpact, 0.12f);
            var inventory = server.System<MarketInventorySystem>();
            var market = server.System<DynamicMarketSystem>();
            var settings = server.System<MarketSettingsSystem>();
            var before = settings.GetSnapshot();
            const string key = "proto:ExodusInventoryFirst";
            inventory.Clear();
            Assert.That(inventory.TryAddStock("ExodusInventoryFirst", 2, 100), Is.True);
            market.SetFactor(key, 1.5);

            server.EntMan.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());

            var after = settings.GetSnapshot();
            Assert.Multiple(() =>
            {
                Assert.That(inventory.GetStock(), Is.Empty);
                Assert.That(market.GetFactor(key), Is.EqualTo(1.5));
                Assert.That(after.Revision, Is.EqualTo(before.Revision));
                Assert.That(after.Global, Is.EqualTo(before.Global));
                Assert.That(after.Overrides, Is.EqualTo(before.Overrides));
                Assert.That(after.GroupOverrides, Is.EquivalentTo(before.GroupOverrides));
            });
        });
        await pair.CleanReturnAsync();
    }
}
