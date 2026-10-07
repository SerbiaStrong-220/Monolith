// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using Content.Server._Exodus.Economy;
using Content.Server._NF.Shipyard;
using Content.Server.Construction.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Stack;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy;
using Content.Shared._Mono.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._Mono.Shipyard;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketShipyardBaselineTest
{
    private const string MachinePrototype = "ExodusShipyardBaselineMachine";
    private const string MachineKey = "proto:" + MachinePrototype;
    private const string DiamondKey = "stack:Diamond";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusShipyardBaselineMachine
          parent: BaseItem
          components:
          - type: Anchorable
          - type: ShipRepairable
            repairCost: 2
          - type: StaticPrice
            price: 100
        """;

    [TestCase(1)]
    [TestCase(2)]
    public async Task ReplacingOriginalEquipmentOnlySellsTheQuantityAboveThePurchasedBaseline(int replacements)
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var original = entities.SpawnEntity(MachinePrototype, coordinates);
            CapturePurchase(entities, grid);
            entities.DeleteEntity(original);

            var sold = new List<EntityUid>();
            for (var index = 0; index < replacements; index++)
                sold.Add(entities.SpawnEntity(MachinePrototype, coordinates));

            AssertSale(entities, grid, sold, MachinePrototype, MachineKey, replacements - 1, market);
        });
    }

    [TestCase(0, false)]
    [TestCase(3, true)]
    [TestCase(7, false)]
    [TestCase(100, true)]
    public async Task MergingAndSplittingStacksPreservesTheExcessQuantity(int splitUnits, bool keepOriginalStack)
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var stacks = entities.System<StackSystem>();
            var original = entities.SpawnEntity("MaterialDiamond1", coordinates);
            stacks.SetCount(original, 7);
            CapturePurchase(entities, grid);
            var added = entities.SpawnEntity("MaterialDiamond1", coordinates);
            stacks.SetCount(added, 100);

            var recipient = keepOriginalStack ? original : added;
            var donor = keepOriginalStack ? added : original;
            entities.GetComponent<StackComponent>(recipient).MaxCountOverride = 200;
            var merge = new InteractUsingEvent(grid, recipient, donor, coordinates);
            entities.EventBus.RaiseLocalEvent(donor, merge);
            Assert.That(merge.Handled, Is.True, "The test must merge the stacks through the normal interaction path.");
            Assert.That(entities.GetComponent<StackComponent>(recipient).Count, Is.EqualTo(107));

            var sold = new List<EntityUid> { recipient };
            if (splitUnits > 0)
            {
                var split = stacks.Split(recipient, splitUnits, coordinates);
                Assert.That(split, Is.Not.Null);
                Assert.That(entities.GetComponent<StackComponent>(split!.Value).Count, Is.EqualTo(splitUnits));
                sold.Insert(0, split.Value);
            }

            AssertSale(entities, grid, sold, "MaterialDiamond1", DiamondKey, 100, market);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FinalPurchaseCapturesFactoryAdditionsOnceAndRepeatedEventsKeepLaterCargoSellable(bool initialLoadEvent)
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var original = entities.SpawnEntity(MachinePrototype, coordinates);
            if (initialLoadEvent)
                entities.EventBus.RaiseLocalEvent(grid, new ShipBoughtEvent());

            // Hull modifications can add equipment between map loading and completion of the purchase.
            var factoryAddition = entities.SpawnEntity(MachinePrototype, coordinates);
            entities.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(grid, grid));
            var addedCargo = entities.SpawnEntity(MachinePrototype, coordinates);

            entities.EventBus.RaiseLocalEvent(grid, new ShipBoughtEvent());
            entities.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(grid, grid));

            AssertSale(entities, grid, [original, factoryAddition, addedCargo], MachinePrototype, MachineKey, 1, market);
        });
    }

    [Test]
    public async Task UpdatingTheRepairSnapshotDoesNotIncreaseThePurchaseBaseline()
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var transforms = entities.System<SharedTransformSystem>();
            var repair = entities.System<SharedShipRepairSystem>();
            var original = entities.SpawnEntity(MachinePrototype, coordinates);
            Assert.That(transforms.AnchorEntity((original, entities.GetComponent<TransformComponent>(original))), Is.True);
            CapturePurchase(entities, grid);
            var initialSnapshot = entities.GetComponent<ShipRepairDataComponent>(grid);
            var initialRevision = initialSnapshot.Revision;
            Assert.That(CountSnapshotEntities(initialSnapshot), Is.EqualTo(1));

            var added = entities.SpawnEntity(MachinePrototype, coordinates);
            Assert.That(transforms.AnchorEntity((added, entities.GetComponent<TransformComponent>(added))), Is.True);
            repair.GenerateRepairData(grid);
            var updatedSnapshot = entities.GetComponent<ShipRepairDataComponent>(grid);
            Assert.That(updatedSnapshot.Revision, Is.GreaterThan(initialRevision));
            Assert.That(CountSnapshotEntities(updatedSnapshot), Is.EqualTo(2),
                "The updated repair snapshot must actually include the extra machine before checking the independent purchase baseline.");

            AssertSale(entities, grid, [original, added], MachinePrototype, MachineKey, 1, market);
        });
    }

    [Test]
    public async Task OrdinaryGoodsSalesDoNotDeductTheShipPurchaseBaseline()
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var original = entities.SpawnEntity(MachinePrototype, coordinates);
            CapturePurchase(entities, grid);
            var expected = ExpectedFactor(market, MachineKey, 1);
            var sale = new MarketGoodsSoldEvent([original], grid);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sale);

            var inventory = entities.System<MarketInventorySystem>();
            Assert.That(inventory.TryGetStock(MachinePrototype, out var stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(1),
                "Purchase allowances apply only when the endpoint sells the ship, not when it sells individual goods.");
            Assert.That(market.GetFactor(MachineKey), Is.EqualTo(expected).Within(1e-10));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RemovedOriginalMachinePartsRemainCoveredByThePurchaseBaseline(bool reverseRoots)
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var original = entities.SpawnEntity("ShieldGeneratorCdm", coordinates);
            var machine = entities.GetComponent<MachineComponent>(original);
            var baskets = entities.System<MarketBasketSystem>();
            Assert.That(baskets.TryGetEntityBasket(original, out var originalBasket, out var failure), Is.True, failure);
            Assert.That(machine.BoardContainer.ContainedEntities, Has.Count.EqualTo(1));
            Assert.That(machine.PartContainer.ContainedEntities, Is.Not.Empty);
            CapturePurchase(entities, grid);

            var sold = new List<EntityUid> { original };
            var containers = entities.System<SharedContainerSystem>();
            foreach (var container in new[] { machine.BoardContainer, machine.PartContainer })
            {
                var contents = new List<EntityUid>(container.ContainedEntities);
                foreach (var part in contents)
                {
                    Assert.That(containers.Remove(part, container, destination: coordinates), Is.True);
                    sold.Add(part);
                }
            }
            Assert.That(machine.BoardContainer.ContainedEntities, Is.Empty);
            Assert.That(machine.PartContainer.ContainedEntities, Is.Empty);
            if (reverseRoots)
                sold.Reverse();

            var sale = new MarketGoodsSoldEvent(sold, grid, OriginalShip: grid);
            for (var notification = 0; notification < 2; notification++)
            {
                entities.EventBus.RaiseEvent(EventSource.Local, ref sale);
                Assert.That(entities.System<MarketInventorySystem>().GetStock(), Is.Empty,
                    "Removing the original board and construction materials must not turn the purchased equipment into new stock.");
                foreach (var line in originalBasket.Lines)
                    Assert.That(market.GetFactor(line.MarketKey), Is.EqualTo(1), line.MarketKey);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OneExtraMachineAndLoosePartAreStockedOnceRegardlessOfRootOrder(bool reverseRoots)
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var original = entities.SpawnEntity("ShieldGeneratorCdm", coordinates);
            var originalMachine = entities.GetComponent<MachineComponent>(original);
            CapturePurchase(entities, grid);
            var added = entities.SpawnEntity("ShieldGeneratorCdm", coordinates);
            var addedMachine = entities.GetComponent<MachineComponent>(added);
            var loosePart = entities.SpawnEntity("SheetSteel1", coordinates);
            entities.System<StackSystem>().SetCount(loosePart, 1);
            var partKey = market.GetMarketKey(loosePart);
            var matchingInstalledUnits = 0;
            foreach (var part in addedMachine.PartContainer.ContainedEntities)
            {
                if (market.GetMarketKey(part) == partKey)
                    matchingInstalledUnits += entities.GetComponent<StackComponent>(part).Count;
            }
            Assert.That(matchingInstalledUnits, Is.EqualTo(10),
                "The loose steel sheet must share its commodity with the ten steel units installed in each machine.");

            var expected = new MarketTransactionState();
            var baskets = entities.System<MarketBasketSystem>();
            foreach (var excess in new[] { added, loosePart })
            {
                Assert.That(baskets.TryGetEntityBasket(excess, out var basket, out var failure), Is.True, failure);
                Assert.That(basket.Exact, Is.True);
                foreach (var line in basket.Lines)
                {
                    market.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice, line.Quantity,
                        1, 1, expected, applyImpact: false);
                }
            }

            var sold = new List<EntityUid> { original, added, loosePart };
            foreach (var machine in new[] { originalMachine, addedMachine })
            {
                sold.AddRange(machine.BoardContainer.ContainedEntities);
                sold.AddRange(machine.PartContainer.ContainedEntities);
            }
            if (reverseRoots)
                sold.Reverse();

            var sale = new MarketGoodsSoldEvent(sold, grid, OriginalShip: grid);
            var inventory = entities.System<MarketInventorySystem>();
            for (var notification = 0; notification < 2; notification++)
            {
                entities.EventBus.RaiseEvent(EventSource.Local, ref sale);
                Assert.That(inventory.GetStock(), Has.Count.EqualTo(2),
                    "The extra machine includes its installed parts; only the additional loose part is a separate product.");
                Assert.That(inventory.TryGetStock("ShieldGeneratorCdm", out var machines), Is.True);
                Assert.That(machines!.Quantity, Is.EqualTo(1));
                Assert.That(inventory.TryGetStock("SheetSteel1", out var parts), Is.True);
                Assert.That(parts!.Quantity, Is.EqualTo(1),
                    "Installed parts must neither become duplicate stock nor consume the allowance for the extra loose part.");
                foreach (var (key, factor) in expected.Factors)
                    Assert.That(market.GetFactor(key), Is.EqualTo(factor).Within(1e-10), key);
            }
        });
    }

    private static void CapturePurchase(IEntityManager entities, EntityUid grid)
    {
        entities.EventBus.RaiseLocalEvent(grid, new ShipBoughtEvent());
        entities.EventBus.RaiseEvent(EventSource.Local, new ShipyardShuttlePurchaseEvent(grid, grid));
    }

    private static int CountSnapshotEntities(ShipRepairDataComponent snapshot)
    {
        var count = 0;
        foreach (var chunk in snapshot.Chunks.Values)
            count += chunk.Entities.Count;
        return count;
    }

    private static double ExpectedFactor(DynamicMarketSystem market, string key, int units)
    {
        if (units == 0)
            return 1;

        var transaction = new MarketTransactionState();
        market.CalculateSequentialSellValue(key, 1, units, 1, 1, transaction, applyImpact: false);
        Assert.That(transaction.Factors[key], Is.LessThan(1));
        Assert.That(market.GetFactor(key), Is.EqualTo(1), "Calculating the expected factor must not commit a sale.");
        return transaction.Factors[key];
    }

    private static void AssertSale(IEntityManager entities, EntityUid grid, IReadOnlyCollection<EntityUid> sold,
        EntProtoId prototype, string key, int expectedUnits, DynamicMarketSystem market)
    {
        var expectedFactor = ExpectedFactor(market, key, expectedUnits);
        var inventory = entities.System<MarketInventorySystem>();
        var sale = new MarketGoodsSoldEvent(sold, grid, OriginalShip: grid);
        for (var notification = 0; notification < 2; notification++)
        {
            entities.EventBus.RaiseEvent(EventSource.Local, ref sale);
            Assert.That(inventory.TryGetStock(prototype, out var stock), Is.EqualTo(expectedUnits > 0));
            if (expectedUnits > 0)
                Assert.That(stock!.Quantity, Is.EqualTo(expectedUnits), "Only the quantity above the purchase baseline may enter stock, exactly once.");
            Assert.That(inventory.GetStock(), Has.Count.EqualTo(expectedUnits > 0 ? 1 : 0));
            Assert.That(market.GetFactor(key), Is.EqualTo(expectedFactor).Within(1e-10),
                "Only the quantity above the purchase baseline may affect quotations, exactly once.");
        }
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
