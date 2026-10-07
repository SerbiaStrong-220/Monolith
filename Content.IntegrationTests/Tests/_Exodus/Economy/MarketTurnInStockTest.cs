// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._NF.Medical;
using Content.Server._NF.Medical.Components;
using Content.Server._NF.Pirate.Components;
using Content.Server._NF.SectorServices;
using Content.Server.Cargo.Systems;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._Exodus.CCVar;
using Content.Shared._NF.Contraband;
using Content.Shared._NF.Contraband.Events;
using Content.Shared._NF.Medical;
using Content.Shared._NF.Medical.Prototypes;
using Content.Shared._NF.Pirate;
using Content.Shared._NF.Pirate.Components;
using Content.Shared._NF.Pirate.Events;
using Content.Shared.Contraband;
using Content.Shared.Damage;
using Content.Shared.Interaction;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketTurnInStockTest
{
    private const string ItemKey = "stack:ExodusTurnInStockStack";
    private const string SuppliesKey = "proto:TradeGoodSupplies";
    [TestPrototypes]
    private const string Prototypes = @"
- type: stack
  id: ExodusTurnInStockStack
  name: stack-steel
  spawn: ExodusTurnInStockItem
  maxCount: 50

- type: entity
  id: ExodusTurnInStockItem
  parent: BaseItem
  components:
  - type: Material
  - type: Stack
    stackType: ExodusTurnInStockStack
    count: 7
  - type: StaticPrice
    price: 10

- type: entity
  id: ExodusTurnInStockContainer
  parent: BaseItem
  components:
  - type: StaticPrice
    price: 5

- type: entity
  id: ExodusTurnInContrabandConsole
  components:
  - type: ContrabandPalletConsole
  - type: UserInterface
    interfaces:
      enum.ContrabandPalletConsoleUiKey.Contraband:
        type: ContrabandPalletConsoleBoundUserInterface

- type: pirateBounty
  id: ExodusTurnInLooseBounty
  reward: 1
  description: pirate-bounty-description-generic
  entries:
  - id: ExodusTurnInStockItem
    amount: 2
    name: cargo-pallet-menu-items-label

- type: pirateBounty
  id: ExodusTurnInCrateBounty
  reward: 1
  description: pirate-bounty-description-generic
  spawnChest: true
  entries:
  - id: ExodusTurnInStockItem
    amount: 2
    name: cargo-pallet-menu-items-label

- type: medicalBounty
  id: ExodusTurnInMedicalBounty
  baseReward: 10
  damageSets: {}
";

    [Test]
    public async Task StationlessContrabandSaleStocksConsumedContentsOnce()
    {
        await RunTurnInTest((entities, coordinates, inventory, prototypes) =>
        {
            entities.SpawnEntity("ContrabandPallet", coordinates);
            var console = entities.SpawnEntity("ExodusTurnInContrabandConsole", coordinates);
            var root = entities.SpawnEntity("ExodusTurnInStockContainer", coordinates);
            var contraband = entities.AddComponent<ContrabandComponent>(root);
            contraband.TurnInValues["FederationMilitaryCredit"] = 1;
            var item = entities.SpawnEntity("ExodusTurnInStockItem", coordinates);
            var containers = entities.System<SharedContainerSystem>();
            Assert.That(containers.Insert(item, containers.EnsureContainer<Container>(root, "test-goods")), Is.True);

            entities.EventBus.RaiseLocalEvent(console, new ContrabandPalletAppraiseMessage { Actor = console });
            Assert.That(inventory.GetStock(), Is.Empty);
            Assert.That(entities.System<DynamicMarketSystem>().GetFactor(ItemKey), Is.EqualTo(2));
            entities.EventBus.RaiseLocalEvent(console, new ContrabandPalletSellMessage { Actor = console });

            Assert.That(entities.Deleted(root), Is.True);
            Assert.That(entities.Deleted(item), Is.True);
            Assert.That(inventory.TryGetStock("ExodusTurnInStockItem", out var stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(7));
            Assert.That(inventory.TryGetStock("ExodusTurnInStockContainer", out _), Is.False);
            AssertSaleImpact(entities, ItemKey, 7);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PirateBountyStocksEntireConsumedStackWithoutDuplicatingCrateContents(bool crate)
    {
        await RunTurnInTest((entities, coordinates, inventory, prototypes) =>
        {
            var host = entities.SpawnEntity(null, coordinates);
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            var service = entities.System<SectorServiceSystem>().GetServiceEntity();
            var database = entities.EnsureComponent<SectorPirateBountyDatabaseComponent>(service);
            database.Bounties.Clear();
            database.MaxBounties = 0;
            database.Bounties.Add(new PirateBountyData
            {
                Id = "EXODUS-TURN-IN",
                Bounty = crate ? "ExodusTurnInCrateBounty" : "ExodusTurnInLooseBounty",
                Accepted = true,
            });
            entities.SpawnEntity("ContrabandPallet", coordinates);
            var console = entities.SpawnEntity(null, coordinates);
            var redemption = entities.AddComponent<PirateBountyRedemptionConsoleComponent>(console);
            redemption.LastRedeemAttempt = TimeSpan.MinValue;
            var item = entities.SpawnEntity("ExodusTurnInStockItem", coordinates);
            entities.AddComponent<PirateBountyItemComponent>(item).ID = "ExodusTurnInStockItem";
            var untouched = entities.SpawnEntity("ExodusTurnInStockItem", coordinates);
            if (crate)
            {
                var root = entities.SpawnEntity("ExodusTurnInStockContainer", coordinates);
                entities.AddComponent<PirateBountyLabelComponent>(root).Id = "EXODUS-TURN-IN";
                var containers = entities.System<SharedContainerSystem>();
                Assert.That(containers.Insert(item, containers.EnsureContainer<Container>(root, "test-goods")), Is.True);
            }

            entities.EventBus.RaiseLocalEvent(console, new PirateBountyRedemptionMessage { Actor = console });

            Assert.That(entities.Deleted(item), Is.True);
            Assert.That(entities.Deleted(untouched), Is.False);
            Assert.That(database.Bounties, Is.Empty);
            Assert.That(inventory.TryGetStock("ExodusTurnInStockItem", out var stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(7));
            AssertSaleImpact(entities, ItemKey, 7);
        });
    }

    [Test]
    public async Task MedicalRedemptionStocksConsumedContentsOnlyOnceBeforeDeletion()
    {
        await RunTurnInTest((entities, coordinates, inventory, prototypes) =>
        {
            var console = entities.SpawnEntity(null, coordinates);
            entities.AddComponent(console, new MedicalBountyRedemptionComponent { BodyContainer = "test-patient" });
            var patient = entities.SpawnEntity("ExodusTurnInStockContainer", coordinates);
            entities.AddComponent<DamageableComponent>(patient);
            entities.AddComponent(patient, new MedicalBountyComponent
            {
                Bounty = prototypes.Index(new ProtoId<MedicalBountyPrototype>("ExodusTurnInMedicalBounty")),
                MaxBountyValue = 10,
                BountyInitialized = true,
            });
            var item = entities.SpawnEntity("ExodusTurnInStockItem", coordinates);
            var containers = entities.System<SharedContainerSystem>();
            Assert.That(containers.Insert(item, containers.EnsureContainer<Container>(patient, "test-goods")), Is.True);
            Assert.That(containers.Insert(patient, containers.EnsureContainer<ContainerSlot>(console, "test-patient")), Is.True);

            entities.EventBus.RaiseLocalEvent(console, new RedeemMedicalBountyMessage { Actor = console });
            entities.EventBus.RaiseLocalEvent(console, new RedeemMedicalBountyMessage { Actor = console });

            Assert.That(entities.IsQueuedForDeletion(patient), Is.True);
            Assert.That(inventory.TryGetStock("ExodusTurnInStockItem", out var stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(7));
            var cash = 0;
            var query = entities.AllEntityQueryEnumerator<StackComponent, TransformComponent>();
            while (query.MoveNext(out _, out var stack, out var transform))
            {
                if (stack.StackTypeId == "Credit" && transform.GridUid == coordinates.EntityId)
                    cash += stack.Count;
            }
            Assert.That(cash, Is.EqualTo(10), "Queued patients must not pay twice in one tick.");
            AssertSaleImpact(entities, ItemKey, 7);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CargoChuteStocksConsumedTradeGoodsOnceOnlyWhenMarked(bool marked)
    {
        await RunTurnInTest((entities, coordinates, inventory, prototypes) =>
        {
            var chute = entities.SpawnEntity("CargoChuteDepot", coordinates);
            if (!marked)
                entities.RemoveComponent<MarketStockSourceComponent>(chute);
            var secondChute = entities.SpawnEntity("CargoChuteDepot", coordinates);
            var item = entities.SpawnEntity("TradeGoodSupplies", coordinates);

            entities.EventBus.RaiseLocalEvent(chute, new InteractUsingEvent(chute, item, chute, coordinates));
            Assert.That(entities.IsQueuedForDeletion(item), Is.True);
            entities.EventBus.RaiseLocalEvent(secondChute, new InteractUsingEvent(secondChute, item, secondChute, coordinates));
            Assert.That(entities.GetComponent<DispenserComponent>(secondChute).Dispensing, Is.False,
                "An item already consumed by another chute cannot start another payout.");
            Assert.That(inventory.TryGetStock("TradeGoodSupplies", out var stock), Is.EqualTo(marked));
            if (marked)
            {
                Assert.That(stock!.Quantity, Is.EqualTo(1));
                AssertSaleImpact(entities, SuppliesKey, 1);
            }
            else
                Assert.That(entities.System<DynamicMarketSystem>().GetFactor(SuppliesKey), Is.EqualTo(2));
        });
    }

    private static void AssertSaleImpact(IEntityManager entities, string key, int units)
    {
        var market = entities.System<DynamicMarketSystem>();
        var transaction = new MarketTransactionState();
        transaction.Set(key, 2);
        market.CalculateSequentialSellValue(key, 1, units, 1, 1, transaction, false);
        Assert.That(transaction.Factors[key], Is.LessThan(2));
        Assert.That(market.GetFactor(key), Is.EqualTo(transaction.Factors[key]).Within(1e-12),
            "Each real turn-in must apply pressure for the consumed quantity exactly once.");
    }

    private static async Task RunTurnInTest(
        Action<IEntityManager, EntityCoordinates, MarketInventorySystem, IPrototypeManager> assertion)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var configuration = server.ResolveDependency<IConfigurationManager>();
            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            configuration.SetCVar(EXCVars.DynamicMarketSellImpact, 0.08f);
            configuration.SetCVar(EXCVars.DynamicMarketBuyImpact, 0.08f);
            configuration.SetCVar(EXCVars.DynamicMarketReferenceVolume, 100f);
            var market = entities.System<DynamicMarketSystem>();
            market.SetFactor(ItemKey, 2);
            market.SetFactor(SuppliesKey, 2);
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            try
            {
                assertion(entities, new EntityCoordinates(map.Grid, 0.5f, 0.5f), inventory,
                    server.ResolveDependency<IPrototypeManager>());
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                inventory.Clear();
            }
        });
        await pair.CleanReturnAsync();
    }
}
