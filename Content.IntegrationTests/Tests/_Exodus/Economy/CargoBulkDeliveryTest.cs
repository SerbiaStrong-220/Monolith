// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.Reflection;
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Construction.Components;
using Content.Server.Labels.Components;
using Content.Server.Station.Systems;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Cargo;
using Content.Shared.Paper;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class CargoBulkDeliveryTest
{
    private const string ItemPrototype = "ExodusBulkDeliveryTestItem";
    private const string StackPrototype = "ExodusBulkDeliveryTestMaterial";
    private const string CratePrototype = "ExodusCargoDeliveryCrate";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusBulkDeliveryTestConsole
  components:
  - type: CargoOrderConsole
  - type: StationTracker

- type: entity
  id: ExodusBulkDeliveryTestItem
  parent: BaseItem
  name: test cargo item

- type: stack
  id: ExodusBulkDeliveryTestStack
  name: stack-steel
  spawn: ExodusBulkDeliveryTestMaterial
  maxCount: 50

- type: entity
  id: ExodusBulkDeliveryTestMaterial
  parent: BaseItem
  name: test cargo material
  components:
  - type: Stack
    stackType: ExodusBulkDeliveryTestStack
    count: 50

- type: entity
  id: ExodusBulkDeliveryTestPartialStack
  parent: ExodusBulkDeliveryTestMaterial
  components:
  - type: Stack
    count: 30
";

    private sealed class DeliveryInsertionVetoSystem : EntitySystem
    {
        public EntityUid? TargetMap;
        public bool RejectFirst;
        public EntityUid? RejectedEntity;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<ContainerManagerComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        }

        private void OnInsertAttempt(Entity<ContainerManagerComponent> ent, ref ContainerIsInsertingAttemptEvent args)
        {
            if (TargetMap == null || Transform(ent).MapUid != TargetMap ||
                MetaData(ent).EntityPrototype?.ID != CratePrototype ||
                args.Container.ID != SharedEntityStorageSystem.ContainerName ||
                !RejectFirst && args.Container.ContainedEntities.Count == 0)
            {
                return;
            }

            if (RejectFirst)
                RejectedEntity = args.EntityUid;
            args.Cancel();
        }
    }

    [Test]
    public async Task BelowThresholdDeliversOneItemAndInvoicePerDispatch()
    {
        await RunDeliveryTest(context =>
        {
            var order = AddOrder(context, ItemPrototype, 9);
            for (var delivered = 1; delivered <= 9; delivered++)
            {
                Assert.That(Dispatch(context), Is.True);
                Assert.That(order.NumDispatched, Is.EqualTo(delivered));
                Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(delivered));
                Assert.That(FindPapers(context), Has.Count.EqualTo(delivered));
                Assert.That(context.Orders.Orders.Contains(order), Is.EqualTo(delivered < 9));
            }

            Assert.That(FindPrototype(context, CratePrototype), Is.Empty);
            Assert.That(Dispatch(context), Is.False);
            Assert.That(FindPapers(context), Has.Count.EqualTo(9));
        });
    }

    [TestCase(10, false, 500)]
    [TestCase(100, false, 5000)]
    [TestCase(10, true, 10)]
    [TestCase(100, true, 100)]
    public async Task BulkMaterialsPreservePurchasedUnitsAndPrintOneAttachedInvoice(int quantity, bool resale, int expectedUnits)
    {
        await RunDeliveryTest(context =>
        {
            var order = AddOrder(context, StackPrototype, quantity, resale);
            Assert.That(Dispatch(context), Is.True);

            var crates = FindPrototype(context, CratePrototype);
            Assert.That(crates, Has.Count.EqualTo(1));
            var storage = context.Entities.GetComponent<EntityStorageComponent>(crates[0]);
            var units = 0;
            foreach (var item in storage.Contents.ContainedEntities)
            {
                Assert.That(context.Entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID, Is.EqualTo(StackPrototype));
                units += context.Entities.GetComponent<StackComponent>(item).Count;
            }

            Assert.That(units, Is.EqualTo(expectedUnits));
            AssertManifest(context, crates[0], order, "test cargo material", quantity);
            Assert.That(order.NumDispatched, Is.EqualTo(quantity));
            Assert.That(context.Orders.Orders, Is.Empty);
            Assert.That(Dispatch(context), Is.False, "A completed bulk order must not be delivered again.");
            Assert.That(FindPrototype(context, CratePrototype), Has.Count.EqualTo(1));
            Assert.That(FindPapers(context), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task BulkNonStackableItemsShareOneCrate()
    {
        await RunDeliveryTest(context =>
        {
            var order = AddOrder(context, ItemPrototype, 10);
            Assert.That(Dispatch(context), Is.True);
            var crates = FindPrototype(context, CratePrototype);
            Assert.That(crates, Has.Count.EqualTo(1));
            var storage = context.Entities.GetComponent<EntityStorageComponent>(crates[0]);
            Assert.That(storage.Contents.ContainedEntities, Has.Count.EqualTo(10));
            foreach (var item in storage.Contents.ContainedEntities)
                Assert.That(context.Entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID, Is.EqualTo(ItemPrototype));

            Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(10));
            AssertManifest(context, crates[0], order, "test cargo item", 10);
            Assert.That(order.NumDispatched, Is.EqualTo(10));
            Assert.That(context.Orders.Orders, Is.Empty);
        });
    }

    [Test]
    public async Task RejectedStackInsertionDoesNotMergeOrDispatchAnUndeliveredLot()
    {
        await RunDeliveryTest(context =>
        {
            var veto = context.Entities.System<DeliveryInsertionVetoSystem>();
            veto.TargetMap = context.Map;
            try
            {
                var order = AddOrder(context, "ExodusBulkDeliveryTestPartialStack", 10);
                Assert.That(Dispatch(context), Is.True);
                var firstCrates = FindPrototype(context, CratePrototype);
                Assert.That(firstCrates, Has.Count.EqualTo(1));
                var firstStorage = context.Entities.GetComponent<EntityStorageComponent>(firstCrates[0]);
                Assert.That(firstStorage.Contents.ContainedEntities, Has.Count.EqualTo(1));
                var firstItem = firstStorage.Contents.ContainedEntities[0];
                Assert.That(context.Entities.GetComponent<StackComponent>(firstItem).Count, Is.EqualTo(30),
                    "Rejecting the next stack must not move twenty of its units into the accepted stack.");
                Assert.That(context.Entities.GetComponent<InsideEntityStorageComponent>(firstItem).Storage,
                    Is.EqualTo(firstCrates[0]));
                Assert.That(order.NumDispatched, Is.EqualTo(1));
                Assert.That(context.Orders.Orders, Does.Contain(order));
                AssertManifest(context, firstCrates[0], order, "test cargo material", 1);

                veto.TargetMap = null;
                Assert.That(Dispatch(context), Is.True);
                var crates = FindPrototype(context, CratePrototype);
                Assert.That(crates, Has.Count.EqualTo(2));
                var secondCrate = crates[0] == firstCrates[0] ? crates[1] : crates[0];
                var secondStorage = context.Entities.GetComponent<EntityStorageComponent>(secondCrate);
                var units = 0;
                foreach (var item in secondStorage.Contents.ContainedEntities)
                {
                    units += context.Entities.GetComponent<StackComponent>(item).Count;
                    Assert.That(context.Entities.GetComponent<InsideEntityStorageComponent>(item).Storage,
                        Is.EqualTo(secondCrate));
                }

                Assert.That(units, Is.EqualTo(270));
                Assert.That(units + context.Entities.GetComponent<StackComponent>(firstItem).Count, Is.EqualTo(300));
                AssertManifest(context, secondCrate, order, "test cargo material", 9, 2);
                Assert.That(order.NumDispatched, Is.EqualTo(10));
                Assert.That(context.Orders.Orders, Is.Empty);
                Assert.That(Dispatch(context), Is.False);
            }
            finally
            {
                veto.TargetMap = null;
            }
        });
    }

    [Test]
    public async Task DisabledPackagingPreservesSingleItemDispatch()
    {
        await RunDeliveryTest(context =>
        {
            context.Orders.BulkPackaging.MinimumQuantity = 0;
            var order = AddOrder(context, ItemPrototype, 10);
            Assert.That(Dispatch(context), Is.True);
            Assert.That(order.NumDispatched, Is.EqualTo(1));
            Assert.That(context.Orders.Orders, Does.Contain(order));
            Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(1));
            Assert.That(FindPrototype(context, CratePrototype), Is.Empty);
            Assert.That(FindPapers(context), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task FullCrateLeavesRemainingItemsQueuedForLaterDelivery()
    {
        await RunDeliveryTest(context =>
        {
            var order = AddOrder(context, ItemPrototype, 105);
            Assert.That(Dispatch(context), Is.True);
            var firstCrates = FindPrototype(context, CratePrototype);
            Assert.That(firstCrates, Has.Count.EqualTo(1));
            var firstStorage = context.Entities.GetComponent<EntityStorageComponent>(firstCrates[0]);
            Assert.That(firstStorage.Contents.ContainedEntities, Has.Count.EqualTo(100));
            Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(100));
            Assert.That(order.NumDispatched, Is.EqualTo(100));
            Assert.That(context.Orders.Orders, Does.Contain(order));
            AssertManifest(context, firstCrates[0], order, "test cargo item", 100);

            // An order that qualified for packaging keeps its remaining five items together.
            Assert.That(Dispatch(context), Is.True);
            var crates = FindPrototype(context, CratePrototype);
            Assert.That(crates, Has.Count.EqualTo(2));
            var secondCrate = crates[0] == firstCrates[0] ? crates[1] : crates[0];
            Assert.That(context.Entities.GetComponent<EntityStorageComponent>(secondCrate).Contents.ContainedEntities,
                Has.Count.EqualTo(5));
            Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(105));
            Assert.That(FindPapers(context), Has.Count.EqualTo(2));
            AssertManifest(context, secondCrate, order, "test cargo item", 5, 2);
            Assert.That(order.NumDispatched, Is.EqualTo(105));
            Assert.That(context.Orders.Orders, Is.Empty);
            Assert.That(Dispatch(context), Is.False);
            Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(105));
        });
    }

    [Test]
    public async Task ExistingCratesAreDeliveredWithoutAnotherCrateAroundThem()
    {
        await RunDeliveryTest(context =>
        {
            var order = AddOrder(context, "CrateGenericSteel", 10);
            Assert.That(Dispatch(context), Is.True);
            Assert.That(order.NumDispatched, Is.EqualTo(1));
            Assert.That(context.Orders.Orders, Does.Contain(order));
            var crates = FindPrototype(context, "CrateGenericSteel");
            Assert.That(crates, Has.Count.EqualTo(1));
            Assert.That(context.Entities.GetComponent<TransformComponent>(crates[0]).ParentUid,
                Is.EqualTo(context.Coordinates.EntityId));
            Assert.That(FindPrototype(context, CratePrototype), Is.Empty);
            Assert.That(FindPapers(context), Has.Count.EqualTo(1));
        });
    }

    [TestCase("ShieldGeneratorCdm", 10)]
    [TestCase("ShieldGeneratorCdm", 0)]
    [TestCase("MobShipRepairDrone", 0)]
    public async Task SingleResoldEquipmentArrivesPackagedAndRemainsUnanchoredAfterUnpacking(string prototype, int bulkThreshold)
    {
        await RunDeliveryTest(context =>
        {
            context.Orders.BulkPackaging.MinimumQuantity = bulkThreshold;
            var order = AddOrder(context, prototype, 1, resale: true);
            Assert.That(Dispatch(context), Is.True);

            var crates = FindPrototype(context, CratePrototype);
            Assert.That(crates, Has.Count.EqualTo(1));
            var storage = context.Entities.GetComponent<EntityStorageComponent>(crates[0]);
            Assert.That(storage.Contents.ContainedEntities, Has.Count.EqualTo(1));
            var shield = storage.Contents.ContainedEntities[0];
            Assert.That(context.Entities.GetComponent<MetaDataComponent>(shield).EntityPrototype?.ID,
                Is.EqualTo(prototype));
            Assert.That(context.Entities.GetComponent<TransformComponent>(shield).Anchored, Is.False);
            Assert.That(context.Entities.GetComponent<InsideEntityStorageComponent>(shield).Storage, Is.EqualTo(crates[0]));
            if (context.Entities.TryGetComponent<MachineComponent>(shield, out var machine))
            {
                Assert.That(machine.BoardContainer.ContainedEntities, Has.Count.EqualTo(1));
                Assert.That(machine.PartContainer.ContainedEntities, Is.Not.Empty);
            }
            Assert.That(order.NumDispatched, Is.EqualTo(1));
            Assert.That(context.Orders.Orders, Is.Empty);
            Assert.That(FindPapers(context), Has.Count.EqualTo(1));

            Assert.That(context.Entities.System<EntityStorageSystem>().Remove(shield, crates[0], storage), Is.True);
            Assert.That(storage.Contents.ContainedEntities, Is.Empty);
            var transform = context.Entities.GetComponent<TransformComponent>(shield);
            Assert.That(transform.GridUid, Is.EqualTo(context.Coordinates.EntityId));
            Assert.That(transform.Anchored, Is.False);
            Assert.That(context.Entities.HasComponent<InsideEntityStorageComponent>(shield), Is.False);
        });
    }

    [Test]
    public async Task RejectedStructurePackagingKeepsThePaidOrderQueuedWithoutDeliveringItOutside()
    {
        await RunDeliveryTest(context =>
        {
            var veto = context.Entities.System<DeliveryInsertionVetoSystem>();
            veto.TargetMap = context.Map;
            veto.RejectFirst = true;
            try
            {
                var order = AddOrder(context, "ShieldGeneratorCdm", 1, resale: true);
                Assert.That(Dispatch(context), Is.False);
                Assert.That(order.NumDispatched, Is.Zero);
                Assert.That(context.Orders.Orders, Does.Contain(order));
                Assert.That(FindPrototype(context, "ShieldGeneratorCdm"), Is.Empty);
                Assert.That(FindPapers(context), Is.Empty);
                Assert.That(veto.RejectedEntity, Is.Not.Null);
                var rejected = veto.RejectedEntity!.Value;
                Assert.That(!context.Entities.EntityExists(rejected) || context.Entities.IsQueuedForDeletion(rejected), Is.True,
                    "A failed shipment must not leave its undelivered machine in nullspace.");

                veto.TargetMap = null;
                Assert.That(Dispatch(context), Is.True);
                Assert.That(order.NumDispatched, Is.EqualTo(1));
                Assert.That(context.Orders.Orders, Is.Empty);
                Assert.That(FindPrototype(context, "ShieldGeneratorCdm"), Has.Count.EqualTo(1));
                Assert.That(FindPapers(context), Has.Count.EqualTo(1));
            }
            finally
            {
                veto.TargetMap = null;
                veto.RejectFirst = false;
                veto.RejectedEntity = null;
            }
        });
    }

    [Test]
    public async Task UnknownProductDoesNotBlockOrConsumeOtherOrders()
    {
        await RunDeliveryTest(context =>
        {
            var unknown = AddOrder(context, "ExodusBulkDeliveryMissingProduct", 10);
            var valid = AddOrder(context, ItemPrototype, 10);

            Assert.That(Dispatch(context), Is.True);
            Assert.That(valid.NumDispatched, Is.EqualTo(10));
            Assert.That(unknown.NumDispatched, Is.Zero);
            Assert.That(context.Orders.Orders, Is.EquivalentTo(new[] { unknown }));
            Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(10));
            var crates = FindPrototype(context, CratePrototype);
            Assert.That(crates, Has.Count.EqualTo(1));
            AssertManifest(context, crates[0], valid, "test cargo item", 10);

            Assert.That(Dispatch(context), Is.False);
            Assert.That(unknown.NumDispatched, Is.Zero);
            Assert.That(context.Orders.Orders, Is.EquivalentTo(new[] { unknown }));
            Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(10));
            Assert.That(FindPapers(context), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task DeliverySkipsForeignAndUnapprovedOrders()
    {
        await RunDeliveryTest(context =>
        {
            var foreignConsole = context.Entities.SpawnEntity(null, context.Coordinates);
            var foreign = AddOrder(context, ItemPrototype, 10);
            foreign.Computer = context.Entities.GetNetEntity(foreignConsole);
            var unapproved = AddOrder(context, ItemPrototype, 10);
            unapproved.Approved = false;
            var approved = AddOrder(context, ItemPrototype, 10);

            Assert.That(Dispatch(context), Is.True);
            Assert.That(approved.NumDispatched, Is.EqualTo(10));
            Assert.That(foreign.NumDispatched, Is.Zero);
            Assert.That(unapproved.NumDispatched, Is.Zero);
            Assert.That(context.Orders.Orders, Is.EquivalentTo(new[] { foreign, unapproved }));
            Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(10));
            Assert.That(FindPapers(context), Has.Count.EqualTo(1));
            Assert.That(Dispatch(context), Is.False);
            Assert.That(FindPrototype(context, ItemPrototype), Has.Count.EqualTo(10));
            Assert.That(FindPapers(context), Has.Count.EqualTo(1));
        });
    }

    private static CargoOrderData AddOrder(DeliveryContext context, string product, int quantity, bool resale = false)
    {
        var order = new CargoOrderData(context.Orders.Orders.Count + 1, product, string.Empty, 100, quantity,
            "Test requester", "Test reason", context.Entities.GetNetEntity(context.Console), resale)
        {
            Approved = true,
            Approver = "Test approver",
        };
        context.Orders.Orders.Add(order);
        return order;
    }

    private static bool Dispatch(DeliveryContext context)
    {
        // Exercise the queue boundary used by telepads, including its accounting and manifest creation.
        var method = typeof(CargoSystem).GetMethod("FulfillNextOrder", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        return (bool) method!.Invoke(context.Entities.System<CargoSystem>(),
        [
            new List<NetEntity> { context.Entities.GetNetEntity(context.Console) },
            context.Orders,
            context.Coordinates,
            "PaperCargoInvoice",
        ])!;
    }

    private static void AssertManifest(DeliveryContext context, EntityUid crate, CargoOrderData order,
        string itemName, int quantity, int totalPapers = 1)
    {
        Assert.That(FindPapers(context), Has.Count.EqualTo(totalPapers));
        var label = context.Entities.GetComponent<PaperLabelComponent>(crate);
        Assert.That(label.LabelSlot.Item, Is.Not.Null, "The single invoice belongs on the delivery crate.");
        var invoice = label.LabelSlot.Item!.Value;
        Assert.That(context.Entities.HasComponent<MarketReceiptComponent>(invoice), Is.True);
        var paper = context.Entities.GetComponent<PaperComponent>(invoice);
        Assert.That(paper.Content, Is.EqualTo(Loc.GetString("cargo-console-paper-print-text",
            ("orderNumber", order.OrderId),
            ("itemName", itemName),
            ("orderQuantity", quantity),
            ("requester", "Test requester"),
            ("reason", "Test reason"),
            ("approver", "Test approver"))));
    }

    private static List<EntityUid> FindPrototype(DeliveryContext context, string prototype)
    {
        var found = new List<EntityUid>();
        foreach (var entity in GetDescendants(context.Entities, context.Map))
        {
            if (context.Entities.GetComponent<MetaDataComponent>(entity).EntityPrototype?.ID == prototype)
                found.Add(entity);
        }

        return found;
    }

    private static List<EntityUid> FindPapers(DeliveryContext context)
    {
        var found = new List<EntityUid>();
        foreach (var entity in GetDescendants(context.Entities, context.Map))
        {
            if (context.Entities.HasComponent<PaperComponent>(entity))
                found.Add(entity);
        }

        return found;
    }

    private static IEnumerable<EntityUid> GetDescendants(IEntityManager entities, EntityUid parent)
    {
        var children = entities.GetComponent<TransformComponent>(parent).ChildEnumerator;
        while (children.MoveNext(out var child))
        {
            yield return child;
            foreach (var descendant in GetDescendants(entities, child))
                yield return descendant;
        }
    }

    private static async Task RunDeliveryTest(Action<DeliveryContext> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            try
            {
                var coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
                var station = entities.SpawnEntity(null, coordinates);
                var orders = entities.AddComponent<StationCargoOrderDatabaseComponent>(station);
                var console = entities.SpawnEntity("ExodusBulkDeliveryTestConsole", coordinates);
                entities.System<StationSystem>().SetStation(console, station);
                assertion(new DeliveryContext(entities, map.MapUid, console, orders, coordinates));
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
            }
        });
        await pair.CleanReturnAsync();
    }

    private readonly record struct DeliveryContext(IEntityManager Entities, EntityUid Map, EntityUid Console,
        StationCargoOrderDatabaseComponent Orders, EntityCoordinates Coordinates);
}
