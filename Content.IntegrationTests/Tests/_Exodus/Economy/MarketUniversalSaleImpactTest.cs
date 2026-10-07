// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Labels;
using Content.Server.Stack;
using Content.Server.Storage.Components;
using Content.IntegrationTests.Pair;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Storage;
using Content.Shared.Tag;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketUniversalSaleImpactTest
{
    private const string DiamondKey = "stack:Diamond";
    private const string CrateKey = "proto:CrateGenericSteel";
    private const string LabelKey = "proto:PaperCargoBountyManifest";
    private const string BackpackKey = "proto:ClothingBackpack";
    private const string DroneKey = "proto:MobShipRepairDrone";
    private const double InitialFactor = 2;

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusUniversalImpactPalletConsole
          components:
          - type: CargoPalletConsole
          - type: UserInterface
            interfaces:
              enum.CargoPalletConsoleUiKey.Sale:
                type: CargoPalletConsoleBoundUserInterface

        - type: Tag
          id: ExodusUniversalImpactBountyDiamond

        - type: cargoBounty
          id: ExodusUniversalImpactBounty
          description: cargo-pallet-menu-items-label
          reward: 1234
          entries:
          - name: cargo-pallet-menu-items-label
            amount: 3
            whitelist:
              tags:
              - ExodusUniversalImpactBountyDiamond
        """;

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task GenericSaleAppliesPressureForActualStackUnitsExactlyOnce(bool contained, bool childFirst)
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var diamond = entities.SpawnEntity("MaterialDiamond1", coordinates);
            entities.System<StackSystem>().SetCount(diamond, 7);
            EntityUid[] roots = [diamond];
            if (contained)
            {
                var crate = entities.SpawnEntity("CrateGenericSteel", coordinates);
                var storage = entities.GetComponent<EntityStorageComponent>(crate);
                Assert.That(entities.System<SharedContainerSystem>().Insert(diamond, storage.Contents), Is.True);
                roots = childFirst ? [diamond, crate] : [crate, diamond];
            }

            var expected = ExpectedFactor(market, 7);
            var sold = new MarketGoodsSoldEvent(roots, grid);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);
            Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(expected).Within(1e-12),
                "A completed non-pallet sale must apply the same seven-unit pressure as a market sale.");

            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);
            var duplicateChild = new MarketGoodsSoldEvent([diamond], grid);
            entities.EventBus.RaiseEvent(EventSource.Local, ref duplicateChild);
            Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(expected).Within(1e-12),
                "Repeated notifications and overlapping roots must not apply pressure for the same goods twice.");
            Assert.That(entities.System<MarketInventorySystem>().TryGetStock("MaterialDiamond1", out var stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(7));
        });
    }

    [Test]
    public async Task RealPalletSaleCommitsItsExistingTransactionOnlyOnce()
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var pallet = entities.SpawnEntity("CargoPalletSell", coordinates);
            Assert.That(entities.GetComponent<TransformComponent>(pallet).Anchored, Is.True);
            var console = entities.SpawnEntity("ExodusUniversalImpactPalletConsole", new EntityCoordinates(grid, 2.5f, 0.5f));
            var diamond = entities.SpawnEntity("MaterialDiamond1", coordinates);
            entities.System<StackSystem>().SetCount(diamond, 7);
            var expected = ExpectedFactor(market, 7);

            entities.EventBus.RaiseLocalEvent(console, new CargoPalletAppraiseMessage());
            Assert.That(entities.System<UserInterfaceSystem>().TryGetUiState<CargoPalletConsoleInterfaceState>(console,
                CargoPalletConsoleUiKey.Sale, out var appraisal), Is.True);
            Assert.That(appraisal!.Count, Is.EqualTo(1));
            Assert.That(appraisal.Appraisal, Is.GreaterThan(0));
            Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(InitialFactor), "Appraisal must not consume market pressure.");

            entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
            Assert.That(entities.Deleted(diamond), Is.True, "Exercise a completed pallet transaction, not a synthetic event.");
            Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(expected).Within(1e-12),
                "Pallets already commit their appraisal transaction; universal intake must not commit another sale.");
            Assert.That(entities.System<MarketInventorySystem>().TryGetStock("MaterialDiamond1", out var stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(7));

            entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
            Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(expected).Within(1e-12),
                "A subsequent empty pallet sale must leave the committed market factor unchanged.");
        });
    }

    [Test]
    public async Task StockFilteredParentStillAppliesPressureToItselfAndContents()
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            var backpack = entities.SpawnEntity("ClothingBackpack", coordinates);
            var diamond = entities.SpawnEntity("MaterialDiamond1", coordinates);
            entities.System<StackSystem>().SetCount(diamond, 7);
            Assert.That(entities.System<SharedContainerSystem>().Insert(diamond,
                entities.GetComponent<StorageComponent>(backpack).Container), Is.True);
            market.SetFactor(BackpackKey, InitialFactor);
            var transaction = new MarketTransactionState();
            market.CalculateSequentialSellValue(BackpackKey, 1, 1, 1, 1, transaction, false);
            var expectedDiamond = ExpectedFactor(market, 7);
            var sold = new MarketGoodsSoldEvent([backpack], grid);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

            Assert.That(market.GetFactor(BackpackKey), Is.EqualTo(transaction.Factors[BackpackKey]).Within(1e-12),
                "A completed sale affects quotes even when the stock admission policy excludes its wrapper.");
            Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(expectedDiamond).Within(1e-12));
            var inventory = entities.System<MarketInventorySystem>();
            Assert.That(inventory.TryGetStock("ClothingBackpack", out _), Is.False);
            Assert.That(inventory.TryGetStock("MaterialDiamond1", out var stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(7));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActivePlayerSubtreeNeverChangesQuotes(bool actor)
    {
        await RunTest((entities, grid, coordinates, market, pair) =>
        {
            var drone = entities.SpawnEntity("MobShipRepairDrone", coordinates);
            var diamond = entities.SpawnEntity("MaterialDiamond1", coordinates);
            entities.System<StackSystem>().SetCount(diamond, 7);
            var containers = entities.System<SharedContainerSystem>();
            Assert.That(containers.Insert(diamond, containers.EnsureContainer<Container>(drone, "market-impact-protected")), Is.True);
            market.SetFactor(DroneKey, InitialFactor);
            var minds = entities.System<SharedMindSystem>();
            var previousActor = pair.Player!.AttachedEntity;
            EntityUid? mind = null;
            try
            {
                if (actor)
                {
                    pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, drone);
                    Assert.That(entities.HasComponent<ActorComponent>(drone), Is.True);
                }
                else
                {
                    mind = minds.CreateMind(null).Owner;
                    minds.TransferTo(mind.Value, drone);
                    Assert.That(entities.GetComponent<MindContainerComponent>(drone).HasMind, Is.True);
                }

                var sold = new MarketGoodsSoldEvent([drone, diamond], grid);
                entities.EventBus.RaiseEvent(EventSource.Local, ref sold);
                Assert.That(market.GetFactor(DroneKey), Is.EqualTo(InitialFactor));
                Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(InitialFactor),
                    "An explicitly listed child of a protected actor or mind must not bypass subtree protection.");
                Assert.That(entities.System<MarketInventorySystem>().GetStock(), Is.Empty);
            }
            finally
            {
                if (actor)
                    pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, previousActor);
                if (mind.HasValue)
                {
                    minds.TransferTo(mind.Value, null, createGhost: false);
                    entities.DeleteEntity(mind.Value);
                }
            }
        }, connected: true);
    }

    [Test]
    public async Task MixedPalletSaleDoesNotSkipBountyGoodsSharingAnAlreadyPricedCommodity()
    {
        await RunTest((entities, grid, coordinates, market) =>
        {
            entities.SpawnEntity("CargoPalletSell", coordinates);
            var console = entities.SpawnEntity("ExodusUniversalImpactPalletConsole", new EntityCoordinates(grid, 2.5f, 0.5f));
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                var database = entities.AddComponent<StationCargoBountyDatabaseComponent>(station);
                database.Bounties.Clear();
                database.MaxBounties = 1;
                database.Bounties.Add(new CargoBountyData
                {
                    Id = "EXODUS-IMPACT",
                    Bounty = "ExodusUniversalImpactBounty",
                });
                var normal = entities.SpawnEntity("MaterialDiamond1", coordinates);
                entities.System<StackSystem>().SetCount(normal, 7);
                var crate = entities.SpawnEntity("CrateGenericSteel", coordinates);
                var bountyDiamond = entities.SpawnEntity("MaterialDiamond1", coordinates);
                entities.System<StackSystem>().SetCount(bountyDiamond, 3);
                Assert.That(entities.System<TagSystem>().AddTag(bountyDiamond, "ExodusUniversalImpactBountyDiamond"), Is.True);
                var containers = entities.System<SharedContainerSystem>();
                Assert.That(containers.Insert(bountyDiamond, entities.GetComponent<EntityStorageComponent>(crate).Contents), Is.True);
                var label = entities.SpawnEntity("PaperCargoBountyManifest", coordinates);
                var labelComponent = entities.GetComponent<CargoBountyLabelComponent>(label);
                labelComponent.Id = "EXODUS-IMPACT";
                labelComponent.AssociatedStationId = station;
                Assert.That(containers.Insert(label, containers.EnsureContainer<ContainerSlot>(crate, LabelSystem.ContainerName)), Is.True);
                Assert.That(entities.System<CargoSystem>().IsBountyComplete(crate, "ExodusUniversalImpactBounty"), Is.True);
                var expected = ExpectedFactor(market, 10);

                entities.EventBus.RaiseLocalEvent(console, new CargoPalletAppraiseMessage());
                Assert.That(entities.System<UserInterfaceSystem>().TryGetUiState<CargoPalletConsoleInterfaceState>(console,
                    CargoPalletConsoleUiKey.Sale, out var appraisal), Is.True);
                Assert.That(appraisal!.Count, Is.EqualTo(2));
                Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(InitialFactor));
                entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());

                Assert.That(entities.Deleted(normal), Is.True);
                Assert.That(entities.Deleted(crate), Is.True);
                Assert.That(entities.Deleted(bountyDiamond), Is.True);
                Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(expected).Within(1e-12),
                    "Normal goods commit seven units; universal intake must still commit the bounty's other three units of that same commodity.");
                Assert.That(entities.System<MarketInventorySystem>().TryGetStock("MaterialDiamond1", out var stock), Is.True);
                Assert.That(stock!.Quantity, Is.EqualTo(10));
            }
            finally
            {
                entities.DeleteEntity(station);
            }
        });
    }

    private static double ExpectedFactor(DynamicMarketSystem market, int units)
    {
        var transaction = new MarketTransactionState();
        market.CalculateSequentialSellValue(DiamondKey, 1, units, 1, 1, transaction, false);
        Assert.That(transaction.Factors[DiamondKey], Is.LessThan(InitialFactor));
        Assert.That(market.GetFactor(DiamondKey), Is.EqualTo(InitialFactor));
        return transaction.Factors[DiamondKey];
    }

    private static Task RunTest(Action<IEntityManager, EntityUid, EntityCoordinates, DynamicMarketSystem> assertion)
    {
        return RunTest((entities, grid, coordinates, market, _) => assertion(entities, grid, coordinates, market), false);
    }

    private static async Task RunTest(Action<IEntityManager, EntityUid, EntityCoordinates, DynamicMarketSystem, TestPair> assertion, bool connected)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = connected });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var configuration = pair.Server.ResolveDependency<IConfigurationManager>();
            var market = entities.System<DynamicMarketSystem>();
            var inventory = entities.System<MarketInventorySystem>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
            var impact = configuration.GetCVar(EXCVars.DynamicMarketSellImpact);
            var reference = configuration.GetCVar(EXCVars.DynamicMarketReferenceVolume);
            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            configuration.SetCVar(EXCVars.DynamicMarketSellImpact, 0.08f);
            configuration.SetCVar(EXCVars.DynamicMarketReferenceVolume, 100f);
            var hadDiamond = market.TryGetQuote(DiamondKey, out var previousDiamond);
            var hadCrate = market.TryGetQuote(CrateKey, out var previousCrate);
            var hadLabel = market.TryGetQuote(LabelKey, out var previousLabel);
            var hadBackpack = market.TryGetQuote(BackpackKey, out var previousBackpack);
            var hadDrone = market.TryGetQuote(DroneKey, out var previousDrone);
            market.SetFactor(DiamondKey, InitialFactor);
            inventory.Clear();
            try
            {
                var maps = entities.System<SharedMapSystem>();
                for (var x = 0; x <= 2; x++)
                {
                    for (var y = -2; y <= 0; y++)
                        maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                }
                assertion(entities, map.Grid.Owner, new EntityCoordinates(map.Grid, 0.5f, 0.5f), market, pair);
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                inventory.Clear();
                if (hadDiamond)
                    market.SetFactor(DiamondKey, previousDiamond.Factor);
                else
                    market.ResetKey(DiamondKey);
                if (hadCrate)
                    market.SetFactor(CrateKey, previousCrate.Factor);
                else
                    market.ResetKey(CrateKey);
                if (hadLabel)
                    market.SetFactor(LabelKey, previousLabel.Factor);
                else
                    market.ResetKey(LabelKey);
                if (hadBackpack)
                    market.SetFactor(BackpackKey, previousBackpack.Factor);
                else
                    market.ResetKey(BackpackKey);
                if (hadDrone)
                    market.SetFactor(DroneKey, previousDrone.Factor);
                else
                    market.ResetKey(DroneKey);
                configuration.SetCVar(EXCVars.DynamicMarketReferenceVolume, reference);
                configuration.SetCVar(EXCVars.DynamicMarketSellImpact, impact);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
