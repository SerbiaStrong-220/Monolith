// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.Reflection;
using Content.Server._Exodus.Economy;
using Content.Shared._Crescent.Dispenser;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketReadinessTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusMarketReadinessGoods
  parent: TradeGoodSupplies
";

    [Test]
    public async Task PendingSavedQuotesRejectPurchasesAndMarketTurnInsWithoutMutation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();
        await PoolManager.WaitUntil(server, () => market.AdminQuotesReady);
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
            var inventory = entities.System<MarketInventorySystem>();
            var purchases = entities.System<MarketPurchaseSystem>();
            var chute = entities.SpawnEntity("CargoChuteDepot", coordinates);
            var dispenser = entities.GetComponent<DispenserComponent>(chute);
            dispenser.Inventory["ExodusMarketReadinessGoods"] = "SpaceCash3000";
            var item = entities.SpawnEntity("ExodusMarketReadinessGoods", coordinates);
            var key = DynamicMarketSystem.ProtoKey("ExodusMarketReadinessGoods");
            Assert.That(market.GetAllQuotes().ContainsKey(key), Is.False);

            // Freeze the real load state within this simulation callback, avoiding startup/DB timing races.
            var loading = GetField("_loadStarted");
            var loaded = GetField("_loadCompleted");
            var reset = GetField("_blockLoadApply");
            var persistence = GetField("_persist");
            var oldLoading = loading.GetValue(market);
            var oldLoaded = loaded.GetValue(market);
            var oldReset = reset.GetValue(market);
            var oldPersistence = persistence.GetValue(market);
            loading.SetValue(market, true);
            loaded.SetValue(market, false);
            reset.SetValue(market, false);
            persistence.SetValue(market, true);
            try
            {
                Assert.That(market.Ready, Is.False);
                Assert.That(purchases.TryQuotePrototypeBuy("ExodusMarketReadinessGoods", 1, 1000, 1, out _), Is.False);
                entities.EventBus.RaiseLocalEvent(chute, new InteractUsingEvent(chute, item, chute, coordinates));

                Assert.That(entities.IsQueuedForDeletion(item), Is.False);
                Assert.That(dispenser.Dispensing, Is.False);
                Assert.That(entities.HasComponent<MarketStockProcessedComponent>(item), Is.False);
                Assert.That(inventory.TryGetStock("ExodusMarketReadinessGoods", out _), Is.False);
                Assert.That(market.GetAllQuotes().ContainsKey(key), Is.False,
                    "An early sale must not create a dirty quote that discards its saved factor.");

                var unmarked = entities.SpawnEntity("CargoChuteDepot", coordinates);
                entities.RemoveComponent<MarketStockSourceComponent>(unmarked);
                var nonMarketItem = entities.SpawnEntity("TradeGoodSupplies", coordinates);
                entities.EventBus.RaiseLocalEvent(unmarked,
                    new InteractUsingEvent(unmarked, nonMarketItem, unmarked, coordinates));
                Assert.That(entities.IsQueuedForDeletion(nonMarketItem), Is.True,
                    "Exchanges outside the global market must remain available.");
                Assert.That(entities.GetComponent<DispenserComponent>(unmarked).Dispensing, Is.True);

                var applyLoadedRows = typeof(DynamicMarketSystem).GetMethod("ApplyLoadedRows",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(applyLoadedRows, Is.Not.Null);
                applyLoadedRows!.Invoke(market,
                    [new List<(string, double, float, DateTime)> { (key, 2.5, 0, DateTime.UtcNow) }]);

                Assert.That(market.Ready, Is.True);
                Assert.That(market.GetFactor(key), Is.EqualTo(2.5));
                Assert.That(purchases.TryQuotePrototypeBuy("ExodusMarketReadinessGoods", 1, 1000, 1, out _), Is.True);
                Assert.That(market.GetFactor(key), Is.EqualTo(2.5), "A purchase preview must preserve the saved quote.");
                entities.EventBus.RaiseLocalEvent(chute, new InteractUsingEvent(chute, item, chute, coordinates));
                Assert.That(entities.IsQueuedForDeletion(item), Is.True);
                Assert.That(dispenser.Dispensing, Is.True);
                Assert.That(inventory.TryGetStock("ExodusMarketReadinessGoods", out var stock), Is.True);
                Assert.That(stock!.Quantity, Is.EqualTo(1));
            }
            finally
            {
                loading.SetValue(market, oldLoading);
                loaded.SetValue(market, oldLoaded);
                reset.SetValue(market, oldReset);
                persistence.SetValue(market, oldPersistence);
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                inventory.Clear();
            }
        });
        await pair.CleanReturnAsync();
    }

    private static FieldInfo GetField(string name)
    {
        var field = typeof(DynamicMarketSystem).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return field!;
    }
}
