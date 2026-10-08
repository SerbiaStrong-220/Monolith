// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.CCVar;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketShipyardReagentTest
{
    private const string ReagentKey = "reagent:Flavorol";

    [TestCase(0)]
    [TestCase(100)]
    public async Task FilledChemMasterUsesCommonReagentPriceAndOnlyExtraVolumeMovesMarket(int originalUnits)
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
            var pricing = entities.System<PricingSystem>();
            var solutions = entities.System<SharedSolutionContainerSystem>();
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
                configuration.SetCVar(EXCVars.DynamicMarketSellImpact, 0.125f);
                configuration.SetCVar(EXCVars.DynamicMarketBuyImpact, 0.125f);
                configuration.SetCVar(EXCVars.DynamicMarketReferenceVolume, 100f);
                configuration.SetCVar(EXCVars.DynamicMarketDecayRate, 0f);
                market.ResetAll();
                market.SetFactor(ReagentKey, 0.5);

                var machine = entities.SpawnEntity("ChemMaster", shipMap.GridCoords);
                var shellPrice = pricing.AppraiseGrid(shipMap.Grid.Owner);
                Assert.That(solutions.EnsureSolutionEntity((machine, null), "buffer", out var solution,
                    FixedPoint2.New(2000)), Is.True);
                if (originalUnits > 0)
                    Assert.That(solutions.TryAddReagent(solution!.Value, "Flavorol", FixedPoint2.New(originalUnits)), Is.True);
                entities.EventBus.RaiseLocalEvent(shipMap.Grid.Owner, new ShipBoughtEvent());
                Assert.That(solutions.TryAddReagent(solution!.Value, "Flavorol", FixedPoint2.New(1000)), Is.True);

                var preserved = entities.SpawnEntity("ChemMaster", shipMap.GridCoords);
                entities.EnsureComponent<ShipyardSellConditionComponent>(preserved).PreserveOnSale = true;
                Assert.That(solutions.EnsureSolutionEntity((preserved, null), "buffer", out var preservedSolution,
                    FixedPoint2.New(1000)), Is.True);
                Assert.That(solutions.TryAddReagent(preservedSolution!.Value, "Flavorol", FixedPoint2.New(1000)), Is.True);

                // Flavorol costs 10 credits/u. Integrate the common 0.5 factor over 1000 extra units
                // with rate 0.125/100; factory contents retain the shipyard's original nominal value.
                var expectedPrice = shellPrice + originalUnits * 10 + 5 * (1 - Math.Exp(-1.25)) / 0.00125;
                var expectedFactor = 0.5 * Math.Exp(-1.25);

                var unsupported = entities.SpawnEntity(null, shipMap.GridCoords);
                Assert.That(solutions.EnsureSolutionEntity((unsupported, null), "contents", out var unsupportedSolution,
                    FixedPoint2.New(10)), Is.True);
                Assert.That(solutions.TryAddReagent(unsupportedSolution!.Value, "Flavorol", FixedPoint2.New(10)), Is.True);
                Assert.That(shipyard.TryAppraiseShuttle(shipMap.Grid.Owner, out var rejectedPreview), Is.False,
                    "Unsupported chemistry cannot bypass the shared reagent quote.");
                var rejected = shipyard.TrySellShuttle(port, shipMap.Grid.Owner, console, out var rejectedBill);
                Assert.Multiple(() =>
                {
                    Assert.That(rejectedPreview, Is.Zero);
                    Assert.That(rejected.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.MessageOverwritten));
                    Assert.That(rejectedBill, Is.Zero);
                    Assert.That(entities.EntityExists(shipStation), Is.True,
                        "Appraisal failure must be detected before deleting the ship's station.");
                    Assert.That(entities.IsQueuedForDeletion(shipMap.Grid.Owner), Is.False);
                    Assert.That(entities.GetComponent<TransformComponent>(preserved).GridUid, Is.EqualTo(shipMap.Grid.Owner));
                    Assert.That(market.GetFactor(ReagentKey), Is.EqualTo(0.5));
                });
                entities.DeleteEntity(unsupported);

                Assert.That(shipyard.TryAppraiseShuttle(shipMap.Grid.Owner, out var preview), Is.True);
                Assert.That(shipyard.TryAppraiseShuttle(shipMap.Grid.Owner, out var repeatedPreview), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(preview, Is.EqualTo(expectedPrice).Within(1e-7));
                    Assert.That(repeatedPreview, Is.EqualTo(preview));
                    Assert.That(market.GetFactor(ReagentKey), Is.EqualTo(0.5),
                        "Opening or refreshing the shipyard quote cannot apply reagent sale pressure.");
                });
                var result = shipyard.TrySellShuttle(port, shipMap.Grid.Owner, console, out var bill);
                Assert.That(result.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.Success));
                Assert.Multiple(() =>
                {
                    Assert.That(bill, Is.EqualTo((int) Math.Floor(expectedPrice)),
                        "Selling a filled machine with a ship must not pay a static reagent price.");
                    Assert.That(market.GetFactor(ReagentKey), Is.EqualTo(expectedFactor).Within(1e-10),
                        "Only the added reagent volume must apply market pressure, exactly once.");
                    Assert.That(entities.GetComponent<TransformComponent>(preserved).GridUid, Is.EqualTo(portMap.Grid.Owner),
                        "Preserved chemicals must leave the ship without being paid or changing the market.");
                });

                var repeated = shipyard.TrySellShuttle(port, shipMap.Grid.Owner, console, out var repeatedBill);
                Assert.Multiple(() =>
                {
                    Assert.That(repeated.Error, Is.EqualTo(ShipyardSystem.ShipyardSaleError.InvalidShip));
                    Assert.That(repeatedBill, Is.Zero);
                    Assert.That(market.GetFactor(ReagentKey), Is.EqualTo(expectedFactor).Within(1e-10));
                });
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
