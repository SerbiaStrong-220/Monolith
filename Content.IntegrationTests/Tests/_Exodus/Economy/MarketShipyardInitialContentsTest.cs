// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Server.Storage.Components;
using Content.Shared._Exodus.CCVar;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketShipyardInitialContentsTest
{
    private const string MachinePrototype = "ExodusShipyardInitialMachine";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusShipyardInitialMachine
          parent: BaseItem
          components:
          - type: Anchorable
          - type: StaticPrice
            price: 100
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task ShipSaleExcludesBoughtContentsButIncludesLaterCargoInsideOriginalContainers(bool addCargo)
    {
        await using var pair = await PoolManager.GetServerClient();
        var portMap = await pair.CreateTestMap();
        var shipMap = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            var stations = entities.System<StationSystem>();
            var shipyard = entities.System<ShipyardSystem>();
            var containers = entities.System<SharedContainerSystem>();
            var stacks = entities.System<StackSystem>();
            var market = entities.System<DynamicMarketSystem>();
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

            inventory.Clear();
            shipyard.SetupShipyardIfNeeded();
            EntityUid? serviceHost = null;
            EntityUid? createdService = null;
            var services = entities.System<SectorServiceSystem>();
            if (!entities.EntityExists(services.GetServiceEntity()))
            {
                serviceHost = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                entities.AddComponent<StationSectorServiceHostComponent>(serviceHost.Value);
                createdService = services.GetServiceEntity();
            }

            var port = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<StationDataComponent>(port);
            stations.AddGridToStation(port, portMap.Grid.Owner);
            var shipStation = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<StationDataComponent>(shipStation);
            stations.AddGridToStation(shipStation, shipMap.Grid.Owner);
            entities.EnsureComponent<ShuttleComponent>(shipMap.Grid.Owner);
            var console = entities.SpawnEntity(null, portMap.GridCoords);
            entities.AddComponent<ShipyardConsoleComponent>(console);
            var portDock = entities.SpawnEntity(null, portMap.GridCoords);
            var shipDock = entities.SpawnEntity(null, shipMap.GridCoords);
            var portDocking = entities.AddComponent<DockingComponent>(portDock);
            var shipDocking = entities.AddComponent<DockingComponent>(shipDock);
            portDocking.DockedWith = shipDock;
            shipDocking.DockedWith = portDock;

            try
            {
                configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
                for (var index = 0; index < definitions.Length; index++)
                    configuration.SetCVar(definitions[index], definitions[index].DefaultValue);
                market.ResetAll();

                var initialDiamond = entities.SpawnEntity("MaterialDiamond1", shipMap.GridCoords);
                stacks.SetCount(initialDiamond, 7);
                var initialMachine = entities.SpawnEntity(MachinePrototype, shipMap.GridCoords);
                var initialCrate = entities.SpawnEntity("CrateGenericSteel", shipMap.GridCoords);
                var crateContents = entities.GetComponent<EntityStorageComponent>(initialCrate).Contents;
                var initialGold = entities.SpawnEntity("IngotGold1", shipMap.GridCoords);
                stacks.SetCount(initialGold, 3);
                Assert.That(containers.Insert(initialGold, crateContents), Is.True);

                // This is the same directed event emitted after the shipyard finishes loading a purchase.
                entities.EventBus.RaiseLocalEvent(shipMap.Grid.Owner, new ShipBoughtEvent());
                if (addCargo)
                {
                    var addedDiamond = entities.SpawnEntity("MaterialDiamond1", shipMap.GridCoords);
                    stacks.SetCount(addedDiamond, 100);
                    var addedGold = entities.SpawnEntity("IngotGold1", shipMap.GridCoords);
                    stacks.SetCount(addedGold, 5);
                    Assert.That(containers.Insert(addedGold, crateContents), Is.True,
                        "New cargo in an original container must remain distinguishable from its purchase contents.");
                }

                var diamondKey = market.GetMarketKey(initialDiamond);
                var goldKey = market.GetMarketKey(initialGold);
                var machineKey = market.GetMarketKey(initialMachine);
                var crateKey = market.GetMarketKey(initialCrate);
                var expected = new MarketTransactionState();
                market.CalculateSequentialSellValue(diamondKey, 1, 100, 1, 1, expected, applyImpact: false);
                market.CalculateSequentialSellValue(goldKey, 1, 5, 1, 1, expected, applyImpact: false);
                var expectedDiamond = addCargo ? expected.Factors[diamondKey] : 1;
                var expectedGold = addCargo ? expected.Factors[goldKey] : 1;
                Assert.That(market.GetFactor(diamondKey), Is.EqualTo(1));
                Assert.That(market.GetFactor(goldKey), Is.EqualTo(1));

                var sale = shipyard.TrySellShuttle(port, shipMap.Grid.Owner, console, out _, paidSale: true);
                Assert.That(sale.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.Success));
                Assert.That(entities.IsQueuedForDeletion(shipMap.Grid.Owner), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(inventory.GetStock(), Has.Count.EqualTo(addCargo ? 2 : 0));
                    Assert.That(inventory.TryGetStock(MachinePrototype, out _), Is.False,
                        "Equipment supplied with a bought ship must not become market stock when the ship is sold.");
                    Assert.That(inventory.TryGetStock("CrateGenericSteel", out _), Is.False);
                    Assert.That(market.GetFactor(machineKey), Is.EqualTo(1));
                    Assert.That(market.GetFactor(crateKey), Is.EqualTo(1));
                    Assert.That(market.GetFactor(diamondKey), Is.EqualTo(expectedDiamond).Within(1e-10));
                    Assert.That(market.GetFactor(goldKey), Is.EqualTo(expectedGold).Within(1e-10));
                });
                if (addCargo)
                {
                    Assert.That(inventory.TryGetStock("MaterialDiamond1", out var diamonds), Is.True);
                    Assert.That(diamonds!.Quantity, Is.EqualTo(100), "The seven purchase units must be excluded.");
                    Assert.That(inventory.TryGetStock("IngotGold1", out var gold), Is.True);
                    Assert.That(gold!.Quantity, Is.EqualTo(5), "The original three units in the same crate must be excluded.");
                }

                var repeated = shipyard.TrySellShuttle(port, shipMap.Grid.Owner, console, out var repeatedBill, paidSale: true);
                Assert.That(repeated.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.InvalidShip));
                Assert.That(repeatedBill, Is.Zero);
                Assert.That(inventory.GetStock(), Has.Count.EqualTo(addCargo ? 2 : 0));
                Assert.That(market.GetFactor(diamondKey), Is.EqualTo(expectedDiamond).Within(1e-10));
                Assert.That(market.GetFactor(goldKey), Is.EqualTo(expectedGold).Within(1e-10));
                if (addCargo)
                {
                    Assert.That(inventory.TryGetStock("MaterialDiamond1", out var diamonds), Is.True);
                    Assert.That(diamonds!.Quantity, Is.EqualTo(100));
                    Assert.That(inventory.TryGetStock("IngotGold1", out var gold), Is.True);
                    Assert.That(gold!.Quantity, Is.EqualTo(5));
                }
            }
            finally
            {
                portDocking.DockedWith = null;
                shipDocking.DockedWith = null;
                if (entities.EntityExists(shipStation))
                    entities.DeleteEntity(shipStation);
                entities.DeleteEntity(port);
                if (serviceHost is { } host)
                    entities.DeleteEntity(host);
                if (createdService is { } service && entities.EntityExists(service))
                    entities.DeleteEntity(service);
                inventory.Clear();
                market.ResetAll();
                for (var index = 0; index < definitions.Length; index++)
                    configuration.SetCVar(definitions[index], previous[index]);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });

        await pair.CleanReturnAsync();
    }
}
