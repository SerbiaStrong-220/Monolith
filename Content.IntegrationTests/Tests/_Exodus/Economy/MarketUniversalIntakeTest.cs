// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._NF.Market.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Construction.Components;
using Content.Server.Stack;
using Content.Server.Station.Systems;
using Content.Shared.Maps;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketUniversalIntakeTest
{
    [TestCase("CargoDepot")]
    [TestCase("CargoDepotAlt")]
    [TestCase(null)]
    public async Task SaleWithoutMarketStationAddsWholeStackToSharedStock(string gameMapId)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            var stations = entities.System<StationSystem>();
            inventory.Clear();
            EntityUid? station = null;
            var diamond = entities.SpawnEntity("MaterialDiamond1", map.GridCoords);
            try
            {
                if (gameMapId != null)
                {
                    var gameMap = prototypes.Index<GameMapPrototype>(gameMapId);
                    station = stations.InitializeNewStation(gameMap.Stations[gameMapId], [map.Grid.Owner]);
                    Assert.That(stations.GetOwningStation(map.Grid.Owner), Is.EqualTo(station));
                    Assert.That(entities.HasComponent<CargoMarketDataComponent>(station.Value), Is.False,
                        "The real depot station has no resale terminal policy; its sales must still reach shared stock.");
                }
                else
                {
                    Assert.That(stations.GetOwningStation(map.Grid.Owner), Is.Null,
                        "A player grid without a station must exercise the stationless sale path.");
                }

                entities.System<StackSystem>().SetCount(diamond, 7);
                Assert.That(entities.GetComponent<StackComponent>(diamond).StackTypeId, Is.EqualTo("Diamond"));
                var sold = new EntitySoldEvent([diamond], map.Grid.Owner);
                entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

                Assert.That(inventory.TryGetStock("MaterialDiamond1", out var stock), Is.True,
                    "A completed sale must replenish sector stock regardless of the selling grid's station or terminals.");
                Assert.Multiple(() =>
                {
                    Assert.That(inventory.GetStock(), Has.Count.EqualTo(1));
                    Assert.That(stock!.Quantity, Is.EqualTo(7));
                    Assert.That(stock.StackPrototype?.Id, Is.EqualTo("Diamond"));
                });
            }
            finally
            {
                entities.DeleteEntity(diamond);
                if (station.HasValue)
                    entities.DeleteEntity(station.Value);
                inventory.Clear();
            }
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DuplicateLegacyAndGenericSaleNotificationsAddStockOnce(bool genericFirst)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var diamond = entities.SpawnEntity("MaterialDiamond1", map.GridCoords);
            try
            {
                entities.System<StackSystem>().SetCount(diamond, 7);
                var legacy = new EntitySoldEvent([diamond], map.Grid.Owner);
                var generic = new MarketGoodsSoldEvent([diamond], map.Grid.Owner);
                if (genericFirst)
                    entities.EventBus.RaiseEvent(EventSource.Local, ref generic);
                entities.EventBus.RaiseEvent(EventSource.Local, ref legacy);
                entities.EventBus.RaiseEvent(EventSource.Local, ref legacy);
                entities.EventBus.RaiseEvent(EventSource.Local, ref generic);
                entities.EventBus.RaiseEvent(EventSource.Local, ref generic);

                Assert.That(inventory.TryGetStock("MaterialDiamond1", out var stock), Is.True);
                Assert.That(stock!.Quantity, Is.EqualTo(7),
                    "Repeated notifications for the same sold entity must not create additional stock.");
            }
            finally
            {
                entities.DeleteEntity(diamond);
                inventory.Clear();
            }
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OverlappingContainerAndItemRootsDoNotDuplicateNestedStack(bool childFirst)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var container = entities.SpawnEntity(null, map.GridCoords);
            var diamond = entities.SpawnEntity("MaterialDiamond1", map.GridCoords);
            try
            {
                entities.System<StackSystem>().SetCount(diamond, 7);
                var containers = entities.System<SharedContainerSystem>();
                Assert.That(containers.Insert(diamond, containers.EnsureContainer<Container>(container, "market-test-contents")), Is.True);
                EntityUid[] roots = childFirst ? [diamond, container] : [container, diamond];
                var sold = new MarketGoodsSoldEvent(roots, map.Grid.Owner);
                entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

                Assert.That(inventory.TryGetStock("MaterialDiamond1", out var stock), Is.True);
                Assert.That(inventory.GetStock(), Has.Count.EqualTo(1));
                Assert.That(stock!.Quantity, Is.EqualTo(7),
                    "A nested stack included explicitly among sale roots must still be stocked exactly once.");
            }
            finally
            {
                entities.DeleteEntity(container);
                if (!entities.Deleted(diamond))
                    entities.DeleteEntity(diamond);
                inventory.Clear();
            }
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PlayerControlledEntityAndItsContentsCannotEnterStock(bool actor)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            var minds = entities.System<SharedMindSystem>();
            inventory.Clear();
            var drone = entities.SpawnEntity("MobShipRepairDrone", map.GridCoords);
            var diamond = entities.SpawnEntity("MaterialDiamond1", map.GridCoords);
            var previousActor = pair.Player!.AttachedEntity;
            EntityUid? mind = null;
            try
            {
                var containers = entities.System<SharedContainerSystem>();
                Assert.That(containers.Insert(diamond, containers.EnsureContainer<Container>(drone, "market-protected-contents")), Is.True);
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

                var sold = new MarketGoodsSoldEvent([drone], map.Grid.Owner);
                entities.EventBus.RaiseEvent(EventSource.Local, ref sold);
                Assert.That(inventory.GetStock(), Is.Empty,
                    "Protecting a player-controlled sale root must also protect its contained possessions.");
                Assert.That(entities.Deleted(drone), Is.False);
                Assert.That(entities.Deleted(diamond), Is.False);
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
                entities.DeleteEntity(drone);
                if (!entities.Deleted(diamond))
                    entities.DeleteEntity(diamond);
                inventory.Clear();
            }
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SoldMachineDoesNotAlsoStockItsInstalledBoard(bool boardFirst)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var shield = entities.SpawnEntity("ShieldGeneratorCdm", map.MapCoords);
            try
            {
                var machine = entities.GetComponent<MachineComponent>(shield);
                Assert.That(machine.BoardContainer.ContainedEntities, Has.Count.EqualTo(1));
                Assert.That(machine.PartContainer.ContainedEntities, Is.Not.Empty);
                var board = machine.BoardContainer.ContainedEntities[0];
                EntityUid[] roots = boardFirst ? [board, shield] : [shield, board];
                var sold = new MarketGoodsSoldEvent(roots, map.Grid.Owner);
                entities.EventBus.RaiseEvent(EventSource.Local, ref sold);
                var duplicateBoard = new MarketGoodsSoldEvent([board], map.Grid.Owner);
                entities.EventBus.RaiseEvent(EventSource.Local, ref duplicateBoard);

                var stock = inventory.GetStock();
                Assert.That(stock, Has.Count.EqualTo(1),
                    "An installed board and machine parts must not become separate products, even when listed as overlapping sale roots.");
                Assert.That(stock[0].Prototype.Id, Is.EqualTo("ShieldGeneratorCdm"));
                Assert.That(stock[0].Quantity, Is.EqualTo(1));
            }
            finally
            {
                entities.DeleteEntity(shield);
                inventory.Clear();
            }
        });

        await pair.CleanReturnAsync();
    }
}
