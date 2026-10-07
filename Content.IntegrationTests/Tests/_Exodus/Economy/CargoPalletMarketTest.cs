// (c) Space Exodus Team - EXDS-RL with CLA
using System.Numerics;
using Content.Server._Exodus.Economy;
using Content.Server._NF.Market.Systems;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Labels;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class CargoPalletMarketTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: Tag
  id: ExodusPalletMarketBountyItem

- type: entity
  id: ExodusPalletMarketConsole
  components:
  - type: CargoPalletConsole
  - type: UserInterface
    interfaces:
      enum.CargoPalletConsoleUiKey.Sale:
        type: CargoPalletConsoleBoundUserInterface

- type: entity
  id: ExodusPalletMarketItem
  parent: BaseItem
  components:
  - type: StaticPrice
    price: 0.4
  - type: Tag
    tags:
    - ExodusPalletMarketBountyItem

- type: entity
  id: ExodusPalletMarketContainer
  parent: BaseItem
  components:
  - type: StaticPrice
    price: 5

- type: entity
  id: ExodusPalletMarketStation
  parent: BaseStationCargoMarket
  components:
  - type: StationTracker

- type: cargoBounty
  id: ExodusPalletMarketBounty
  description: cargo-pallet-menu-items-label
  reward: 1234
  entries:
  - name: cargo-pallet-menu-items-label
    amount: 1
    whitelist:
      # TestPrototypes are loaded globally; production cargo must not satisfy this test bounty.
      tags:
      - ExodusPalletMarketBountyItem
";

    private sealed class PalletPriceOverrideSystem : EntitySystem
    {
        public EntityUid? Target;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ContainerManagerComponent, PriceCalculationEvent>(OnPrice);
        }

        private void OnPrice(Entity<ContainerManagerComponent> ent, ref PriceCalculationEvent args)
        {
            if (ent.Owner != Target)
                return;

            args.Price = 17;
            args.Handled = true;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AppraisalRowsMatchRoundedTotalAndOpaqueContents(bool handled)
    {
        await RunPalletTest((entities, console, coordinates, market) =>
        {
            var first = entities.SpawnEntity(handled ? "ExodusPalletMarketContainer" : "ExodusPalletMarketItem", coordinates);
            var second = entities.SpawnEntity("ExodusPalletMarketItem", coordinates);
            var overrides = entities.System<PalletPriceOverrideSystem>();
            if (handled)
            {
                var containers = entities.System<SharedContainerSystem>();
                Assert.That(containers.Insert(second, containers.EnsureContainer<Container>(first, "test-cargo")), Is.True);
                overrides.Target = first;
            }
            else
            {
                entities.SpawnEntity("ExodusPalletMarketItem", coordinates);
            }

            try
            {
                var state = Appraise(entities, console);
                var expected = handled ? 16 : 1;
                Assert.That(state.Appraisal, Is.EqualTo(expected));
                Assert.That(state.Items, Has.Count.EqualTo(1));
                Assert.That(state.Items![0].Price, Is.EqualTo(expected));
                Assert.That(state.Items[0].Quantity, Is.EqualTo(handled ? 1 : 3));
                Assert.That(state.Items[0].UnitPrice, Is.EqualTo(handled ? 16 : 1.0 / 3).Within(0.000001));
                Assert.That(market.GetFactor(market.GetMarketKey(second)), Is.EqualTo(1));
            }
            finally
            {
                overrides.Target = null;
            }
        });
    }

    [Test]
    public async Task TestBountyOnlyAcceptsItsDedicatedTestItem()
    {
        await RunPalletTest((entities, _, coordinates, _) =>
        {
            var cargo = entities.System<CargoSystem>();
            var productionCargo = entities.SpawnEntity("CrateServiceBox", coordinates);
            var testItem = entities.SpawnEntity("ExodusPalletMarketItem", coordinates);

            Assert.That(cargo.IsBountyComplete(productionCargo, "ExodusPalletMarketBounty"), Is.False,
                "Globally loaded test bounties must not introduce arbitrage for production cargo.");
            Assert.That(cargo.IsBountyComplete(testItem, "ExodusPalletMarketBounty"), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CompletedBountyKeepsFixedRewardAndNestedBountyIsNotSold(bool nested)
    {
        await RunPalletTest((entities, console, coordinates, market) =>
        {
            var station = entities.SpawnEntity(null, coordinates);
            var database = entities.AddComponent<StationCargoBountyDatabaseComponent>(station);
            database.Bounties.Clear();
            database.MaxBounties = 1;
            database.Bounties.Add(new CargoBountyData
            {
                Id = "EXODUS-TEST",
                Bounty = "ExodusPalletMarketBounty",
            });

            var crate = entities.SpawnEntity("ExodusPalletMarketContainer", coordinates);
            var item = entities.SpawnEntity("ExodusPalletMarketItem", coordinates);
            var containers = entities.System<SharedContainerSystem>();
            Assert.That(containers.Insert(item, containers.EnsureContainer<Container>(crate, "test-cargo")), Is.True);
            var label = entities.SpawnEntity("PaperCargoBountyManifest", coordinates);
            var labelComp = entities.GetComponent<CargoBountyLabelComponent>(label);
            labelComp.Id = "EXODUS-TEST";
            labelComp.AssociatedStationId = station;
            Assert.That(containers.Insert(label, containers.EnsureContainer<ContainerSlot>(crate, LabelSystem.ContainerName)), Is.True);
            var itemKey = market.GetMarketKey(item);
            market.SetFactor(itemKey, 3);

            if (nested)
            {
                var outer = entities.SpawnEntity("ExodusPalletMarketContainer", coordinates);
                Assert.That(containers.Insert(crate, containers.EnsureContainer<Container>(outer, "test-cargo")), Is.True);
            }

            var expected = nested ? 0 : 1234;
            var expectedFactor = 3.0;
            if (!nested)
            {
                var transaction = new MarketTransactionState();
                market.CalculateSequentialSellValue(itemKey, 1, 1, 1, 1, transaction, applyImpact: false);
                expectedFactor = transaction.Factors[itemKey];
            }
            var appraisal = Appraise(entities, console);
            Assert.That(appraisal.Appraisal, Is.EqualTo(expected));
            Assert.That(appraisal.Count, Is.EqualTo(nested ? 0 : 1));
            Assert.That(Appraise(entities, console).Appraisal, Is.EqualTo(expected), "Appraisal must not consume a bounty quote.");
            Assert.That(market.GetFactor(itemKey), Is.EqualTo(3), "Appraisal must not apply pressure to bounty contents.");
            entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
            Assert.That(market.GetFactor(itemKey), Is.EqualTo(expectedFactor).Within(1e-12),
                "A completed fixed-reward sale applies pressure for its actual contents; a rejected nested bounty does not.");
            Assert.That(entities.Deleted(crate), Is.EqualTo(!nested));
            Assert.That(entities.Deleted(station), Is.False, "The unpriced station entity must remain on the pallet.");
        });
    }

    [TestCase(0)]
    [TestCase(45)]
    [TestCase(90)]
    public async Task PalletLookupUsesGridCoordinatesForNestedConsole(int rotation)
    {
        await RunPalletTest((entities, console, coordinates, _) =>
        {
            var transforms = entities.System<SharedTransformSystem>();
            transforms.SetLocalPosition(coordinates.EntityId, new Vector2(100, -30));
            transforms.SetLocalRotation(coordinates.EntityId, Angle.FromDegrees(rotation));
            var parent = entities.SpawnEntity(null, new EntityCoordinates(coordinates.EntityId, 2.5f, -1.5f));
            transforms.SetLocalRotation(parent, Angle.FromDegrees(90));
            transforms.SetCoordinates(console, new EntityCoordinates(parent, 2, 0));
            Assert.That(entities.GetComponent<TransformComponent>(console).GridUid, Is.EqualTo(coordinates.EntityId));

            for (var i = 0; i < 3; i++)
            {
                entities.SpawnEntity("ExodusPalletMarketItem", coordinates);
            }

            var appraisal = Appraise(entities, console);
            Assert.That(appraisal.Count, Is.EqualTo(3));
            Assert.That(appraisal.Appraisal, Is.EqualTo(1));
            Assert.That(appraisal.Items, Has.Count.EqualTo(1));
            Assert.That(appraisal.Items![0].Quantity, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task GroupedAppraisalSaturatesAtPayoutLimit()
    {
        await RunPalletTest((entities, console, coordinates, _) =>
        {
            for (var i = 0; i < 2; i++)
            {
                var item = entities.SpawnEntity("ExodusPalletMarketItem", coordinates);
                entities.GetComponent<StaticPriceComponent>(item).Price = 3_000_000_000;
            }

            var state = Appraise(entities, console);
            Assert.That(state.Appraisal, Is.EqualTo(int.MaxValue));
            Assert.That(state.Items, Has.Count.EqualTo(1));
            Assert.That(state.Items![0].Price, Is.EqualTo(int.MaxValue));
            Assert.That(state.Items[0].Quantity, Is.EqualTo(2));
        });
    }

    [TestCase("MobShipRepairDrone")]
    [TestCase("MobShipRepairDroneFleetTSF")]
    [TestCase("MobShipRepairDroneFleetPDV")]
    [TestCase("MobShipRepairDroneAsakim")]
    public async Task AutonomousRepairDroneCanBeAppraisedSoldAndStocked(string prototype)
    {
        await RunPalletTest((entities, console, coordinates, market) =>
        {
            var station = CreateMarketStation(entities, coordinates.EntityId);
            var resale = entities.System<MarketSystem>();
            var drone = entities.SpawnEntity(prototype, coordinates);
            Assert.That(entities.GetComponent<MobStateComponent>(drone).CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(resale.TryGetStock(station, prototype, out _), Is.False);

            var appraisal = Appraise(entities, console);
            Assert.That(appraisal.Appraisal, Is.GreaterThan(0));
            Assert.That(appraisal.Count, Is.EqualTo(1));

            entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());

            Assert.That(entities.Deleted(drone), Is.True);
            Assert.That(resale.TryGetStock(station, prototype, out var stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(1));
            Assert.That(stock.Price, Is.GreaterThan(0));
            Assert.That(Appraise(entities, console).Count, Is.Zero);
        });
    }

    [Test]
    public async Task LivingMobsOccupiedDronesAndBlacklistedDronesCannotBeSold()
    {
        await RunPalletTest((entities, console, coordinates, market) =>
        {
            var station = CreateMarketStation(entities, coordinates.EntityId);
            var ordinaryMob = entities.SpawnEntity("MobHuman", coordinates);
            entities.EnsureComponent<StaticPriceComponent>(ordinaryMob).Price = 100;
            var occupiedDrone = entities.SpawnEntity("MobShipRepairDrone", coordinates);
            var blacklistedDrone = entities.SpawnEntity("MobShipRepairDroneFleetTSF", coordinates);
            entities.AddComponent<CargoSellBlacklistComponent>(blacklistedDrone);
            var minds = entities.System<SharedMindSystem>();
            var mind = minds.CreateMind(null).Owner;
            try
            {
                minds.TransferTo(mind, occupiedDrone);
                Assert.That(entities.GetComponent<MindContainerComponent>(occupiedDrone).Mind, Is.EqualTo(mind));
                Assert.That(entities.GetComponent<MobStateComponent>(ordinaryMob).CurrentState, Is.EqualTo(MobState.Alive));
                Assert.That(entities.GetComponent<MobStateComponent>(occupiedDrone).CurrentState, Is.EqualTo(MobState.Alive));

                var appraisal = Appraise(entities, console);
                Assert.That(appraisal.Appraisal, Is.Zero);
                Assert.That(appraisal.Count, Is.Zero);
                entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());

                Assert.That(entities.Deleted(ordinaryMob), Is.False);
                Assert.That(entities.Deleted(occupiedDrone), Is.False);
                Assert.That(entities.Deleted(blacklistedDrone), Is.False);
                var resale = entities.System<MarketSystem>();
                Assert.That(resale.TryGetStock(station, "MobShipRepairDrone", out _), Is.False);
                Assert.That(resale.TryGetStock(station, "MobShipRepairDroneFleetTSF", out _), Is.False);
            }
            finally
            {
                minds.TransferTo(mind, null, createGhost: false);
                entities.DeleteEntity(mind);
            }
        });
    }

    private static EntityUid CreateMarketStation(IEntityManager entities, EntityUid grid)
    {
        var station = entities.SpawnEntity("ExodusPalletMarketStation", new EntityCoordinates(grid, 2.5f, -1.5f));
        var stations = entities.System<StationSystem>();
        stations.SetStation(station, station);
        entities.EnsureComponent<StationTrackerComponent>(grid);
        stations.SetStation(grid, station);
        return station;
    }

    private static CargoPalletConsoleInterfaceState Appraise(IEntityManager entities, EntityUid console)
    {
        entities.EventBus.RaiseLocalEvent(console, new CargoPalletAppraiseMessage());
        Assert.That(entities.System<UserInterfaceSystem>().TryGetUiState<CargoPalletConsoleInterfaceState>(console,
            CargoPalletConsoleUiKey.Sale, out var state), Is.True);
        return state!;
    }

    private static async Task RunPalletTest(Action<IEntityManager, EntityUid, EntityCoordinates, DynamicMarketSystem> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var market = entities.System<DynamicMarketSystem>();
            var inventory = entities.System<MarketInventorySystem>();
            var enabled = cfg.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = cfg.GetCVar(EXCVars.DynamicMarketPersist);
            var impact = cfg.GetCVar(EXCVars.DynamicMarketSellImpact);
            cfg.SetCVar(EXCVars.DynamicMarketPersist, false);
            cfg.SetCVar(EXCVars.DynamicMarketEnabled, true);
            cfg.SetCVar(EXCVars.DynamicMarketSellImpact, 0.08f);
            market.ResetAll();
            inventory.Clear();
            try
            {
                // Spawn coordinates outside the floor are reparented to the map, losing GridUid.
                var maps = entities.System<SharedMapSystem>();
                for (var x = 0; x <= 2; x++)
                {
                    for (var y = -2; y <= 0; y++)
                    {
                        maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                    }
                }

                var coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
                var pallet = entities.SpawnEntity("CargoPalletSell", coordinates);
                Assert.That(entities.GetComponent<TransformComponent>(pallet).Anchored, Is.True);
                var console = entities.SpawnEntity("ExodusPalletMarketConsole", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
                Assert.That(entities.GetComponent<TransformComponent>(console).GridUid, Is.EqualTo(map.Grid.Owner));
                assertion(entities, console, coordinates, market);
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                market.ResetAll();
                inventory.Clear();
                cfg.SetCVar(EXCVars.DynamicMarketSellImpact, impact);
                cfg.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                cfg.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
