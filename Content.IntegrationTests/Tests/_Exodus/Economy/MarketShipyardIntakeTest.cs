// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.CCVar;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketShipyardIntakeTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task OnlyPaidShuttleSalesStockAndApplyImpactToUnpreservedContentsOnce(bool paidSale)
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
            for (var i = 0; i < definitions.Length; i++)
                previous[i] = configuration.GetCVar(definitions[i]);

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

            // The sale's docking precondition depends on this relationship, not physical alignment.
            portDocking.DockedWith = shipDock;
            shipDocking.DockedWith = portDock;
            var goods = entities.SpawnEntity(null, shipMap.GridCoords);
            var diamond = entities.SpawnEntity("MaterialDiamond1", shipMap.GridCoords);
            entities.System<StackSystem>().SetCount(diamond, 100);
            Assert.That(containers.Insert(diamond, containers.EnsureContainer<Container>(goods, "sale-goods")), Is.True);

            var preserved = entities.SpawnEntity(null, shipMap.GridCoords);
            entities.AddComponent<ShipyardSellConditionComponent>(preserved).PreserveOnSale = true;
            var gold = entities.SpawnEntity("IngotGold1", shipMap.GridCoords);
            entities.System<StackSystem>().SetCount(gold, 3);
            Assert.That(containers.Insert(gold, containers.EnsureContainer<Container>(preserved, "preserved-goods")), Is.True);

            try
            {
                configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
                for (var i = 0; i < definitions.Length; i++)
                    configuration.SetCVar(definitions[i], definitions[i].DefaultValue);
                market.ResetAll();
                var diamondKey = market.GetMarketKey(diamond);
                var goldKey = market.GetMarketKey(gold);
                var expected = new MarketTransactionState();
                market.CalculateSequentialSellValue(diamondKey, 1, 100, 1, 1, expected, applyImpact: false);
                var expectedFactor = paidSale ? expected.Factors[diamondKey] : 1;
                Assert.That(expected.Factors[diamondKey], Is.LessThan(1),
                    "The regression must exercise a commodity group with nonzero sale pressure.");
                Assert.That(market.GetFactor(diamondKey), Is.EqualTo(1), "Quoting must not apply the sale.");

                var result = shipyard.TrySellShuttle(port, shipMap.Grid.Owner, console, out _, paidSale);
                Assert.That(result.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.Success));
                Assert.That(entities.IsQueuedForDeletion(shipMap.Grid.Owner), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(entities.EntityExists(gold), Is.True);
                    Assert.That(entities.GetComponent<TransformComponent>(preserved).GridUid, Is.EqualTo(portMap.Grid.Owner));
                    Assert.That(entities.GetComponent<TransformComponent>(gold).GridUid, Is.EqualTo(portMap.Grid.Owner));
                    Assert.That(inventory.TryGetStock("IngotGold1", out _), Is.False,
                        "Preserved containers and their contents must not also be stocked.");
                    Assert.That(inventory.TryGetStock("MaterialDiamond1", out _), Is.EqualTo(paidSale),
                        "A voucher return must not contribute goods to the resale inventory.");
                    Assert.That(inventory.GetStock(), Has.Count.EqualTo(paidSale ? 1 : 0),
                        "The grid, docks and unprototyped wrappers are not resale products.");
                    Assert.That(market.GetFactor(diamondKey), Is.EqualTo(expectedFactor).Within(1e-10),
                        "Paid shuttle contents must apply their group's sale impact exactly once; voucher returns must not.");
                    Assert.That(market.GetFactor(goldKey), Is.EqualTo(1),
                        "Preserved contents must not affect market quotes.");
                });
                if (paidSale)
                {
                    Assert.That(inventory.TryGetStock("MaterialDiamond1", out var stock), Is.True);
                    Assert.That(stock!.Quantity, Is.EqualTo(100));
                }

                var repeated = shipyard.TrySellShuttle(port, shipMap.Grid.Owner, console, out var repeatedBill, paidSale);
                Assert.Multiple(() =>
                {
                    Assert.That(repeated.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.InvalidShip));
                    Assert.That(repeatedBill, Is.Zero);
                    Assert.That(inventory.GetStock(), Has.Count.EqualTo(paidSale ? 1 : 0));
                    Assert.That(market.GetFactor(diamondKey), Is.EqualTo(expectedFactor).Within(1e-10),
                        "A repeated queued sale cannot apply the contents' sale pressure twice.");
                    Assert.That(market.GetFactor(goldKey), Is.EqualTo(1));
                });
                if (paidSale)
                {
                    Assert.That(inventory.TryGetStock("MaterialDiamond1", out var stock), Is.True);
                    Assert.That(stock!.Quantity, Is.EqualTo(100), "A repeated queued sale cannot stock the same contents twice.");
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
                for (var i = 0; i < definitions.Length; i++)
                    configuration.SetCVar(definitions[i], previous[i]);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });

        await pair.CleanReturnAsync();
    }
}
