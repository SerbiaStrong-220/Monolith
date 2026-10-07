// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using Content.Server._Exodus.Economy;
using Content.Server._NF.Shipyard;
using Content.Server.Shuttles.Components;
using Content.Server.Stack;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy;
using Content.Shared._Mono.Shipyard;
using Content.Shared.Materials;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketShipyardMaterialBaselineTest
{
    private const string StoragePrototype = "ExodusShipyardBaselineMaterialStorage";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusShipyardBaselineMaterialStorage
          components:
          - type: Anchorable
          - type: StaticPrice
            price: 100
          - type: MaterialStorage
        """;

    [Test]
    public async Task MissingOriginalCommodityDoesNotReduceAnotherCommodityExcess()
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var stacks = entities.System<StackSystem>();
            var originalDiamond = entities.SpawnEntity("MaterialDiamond1", coordinates);
            stacks.SetCount(originalDiamond, 7);
            var originalGold = entities.SpawnEntity("IngotGold1", coordinates);
            stacks.SetCount(originalGold, 3);
            CapturePurchase(entities, grid);

            entities.DeleteEntity(originalDiamond);
            var addedGold = entities.SpawnEntity("IngotGold1", coordinates);
            stacks.SetCount(addedGold, 100);
            var expected = new MarketTransactionState();
            market.CalculateSequentialSellValue("stack:Gold", 1, 100, 1, 1, expected, applyImpact: false);
            var inventory = entities.System<MarketInventorySystem>();
            var sale = new MarketGoodsSoldEvent([originalGold, addedGold], grid, OriginalShip: grid);
            for (var notification = 0; notification < 2; notification++)
            {
                entities.EventBus.RaiseEvent(EventSource.Local, ref sale);
                Assert.That(inventory.GetStock(), Has.Count.EqualTo(1));
                Assert.That(inventory.TryGetStock("IngotGold1", out var gold), Is.True);
                Assert.That(gold!.Quantity, Is.EqualTo(100),
                    "Seven missing diamonds cannot offset any of the one hundred extra gold units.");
                Assert.That(inventory.TryGetStock("MaterialDiamond1", out _), Is.False);
                Assert.That(market.GetFactor("stack:Diamond"), Is.EqualTo(1));
                Assert.That(market.GetFactor("stack:Gold"), Is.EqualTo(expected.Factors["stack:Gold"]).Within(1e-10));
            }
        });
    }

    [TestCase(0)]
    [TestCase(37)]
    public async Task StoredMaterialBaselinePreservesNativeRemainderAndSellsOnlyAddedUnits(int originalRemainder)
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var sheet = entities.SpawnEntity("SheetSteel1", coordinates);
            var nativePerUnit = entities.GetComponent<PhysicalCompositionComponent>(sheet).MaterialComposition["Steel"];
            Assert.That(nativePerUnit, Is.GreaterThan(originalRemainder));
            entities.DeleteEntity(sheet);

            var storage = entities.SpawnEntity(StoragePrototype, coordinates);
            var materials = entities.System<SharedMaterialStorageSystem>();
            var originalNativeAmount = 7 * nativePerUnit + originalRemainder;
            Assert.That(materials.TryChangeMaterialAmount(storage, "Steel", originalNativeAmount, localOnly: true), Is.True);
            CapturePurchase(entities, grid);
            Assert.That(materials.TryChangeMaterialAmount(storage, "Steel", 100 * nativePerUnit, localOnly: true), Is.True);
            Assert.That(materials.GetMaterialAmount(storage, "Steel", localOnly: true),
                Is.EqualTo(107 * nativePerUnit + originalRemainder));

            var expected = new MarketTransactionState();
            market.CalculateSequentialSellValue("stack:Steel", 1, 100, 1, 1, expected, applyImpact: false);
            var inventory = entities.System<MarketInventorySystem>();
            var sale = new MarketGoodsSoldEvent([storage], grid, OriginalShip: grid);
            for (var notification = 0; notification < 2; notification++)
            {
                entities.EventBus.RaiseEvent(EventSource.Local, ref sale);
                Assert.That(inventory.GetStock(), Has.Count.EqualTo(1));
                Assert.That(inventory.TryGetStock("SheetSteel1", out var steel), Is.True);
                Assert.That(steel!.Quantity, Is.EqualTo(100));
                Assert.That(steel.StackPrototype?.Id, Is.EqualTo("Steel"));
                Assert.That(inventory.TryGetStock(StoragePrototype, out _), Is.False);
                Assert.That(materials.GetMaterialAmount(storage, "Steel", localOnly: true), Is.EqualTo(originalNativeAmount),
                    "Intake consumes only the additional whole sheets; the purchase amount and its native-unit remainder stay untouched.");
                Assert.That(market.GetFactor("stack:Steel"), Is.EqualTo(expected.Factors["stack:Steel"]).Within(1e-10),
                    "The original fractional sheet must not be rounded out of the pressure allowance.");
                Assert.That(market.GetFactor("proto:" + StoragePrototype), Is.EqualTo(1));
            }
        });
    }

    private static void CapturePurchase(IEntityManager entities, EntityUid grid)
    {
        entities.EventBus.RaiseLocalEvent(grid, new ShipBoughtEvent());
        entities.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(grid, grid));
    }

    private static async Task RunTest(Action<IEntityManager, EntityUid, EntityCoordinates, DynamicMarketSystem> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var market = entities.System<DynamicMarketSystem>();
            var inventory = entities.System<MarketInventorySystem>();
            var configuration = pair.Server.ResolveDependency<IConfigurationManager>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
            var definitions = new[]
            {
                EXCVars.DynamicMarketMinFactor,
                EXCVars.DynamicMarketMaxFactor,
                EXCVars.DynamicMarketSellImpact,
                EXCVars.DynamicMarketBuyImpact,
                EXCVars.DynamicMarketReferenceVolume,
                EXCVars.DynamicMarketDecayRate,
            };
            var previous = new float[definitions.Length];
            for (var index = 0; index < definitions.Length; index++)
                previous[index] = configuration.GetCVar(definitions[index]);
            var previousQuotes = new Dictionary<string, MarketQuote>(market.GetAllQuotes());

            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            for (var index = 0; index < definitions.Length; index++)
                configuration.SetCVar(definitions[index], definitions[index].DefaultValue);
            market.ResetAll();
            inventory.Clear();
            try
            {
                entities.EnsureComponent<ShuttleComponent>(map.Grid.Owner);
                assertion(entities, map.Grid.Owner, new EntityCoordinates(map.Grid, 0.5f, 0.5f), market);
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                inventory.Clear();
                for (var index = 0; index < definitions.Length; index++)
                    configuration.SetCVar(definitions[index], previous[index]);
                market.ResetAll();
                foreach (var (key, quote) in previousQuotes)
                    market.SetFactor(key, quote.Factor);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
