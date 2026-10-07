// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Systems;
using Content.Server.Construction.Components;
using Content.Server.Station.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketStructureStockTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusStructureStockStation
  parent: BaseStationCargoMarket
  components:
  - type: StationTracker

- type: entity
  id: ExodusStructureStockAllowed
  parent: BaseItem
  components:
  - type: Anchorable
  - type: StaticPrice
    price: 100

- type: entity
  id: ExodusStructureStockGun
  parent: ExodusStructureStockAllowed
  components:
  - type: Gun

- type: entity
  id: ExodusStructureStockStorage
  parent: ExodusStructureStockAllowed
  components:
  - type: Storage

- type: entity
  id: ExodusStructureStockContraband
  parent: ExodusStructureStockAllowed
  components:
  - type: Contraband
";

    [Test]
    public async Task SoldShieldIsStockedOnceWithoutSeparateMachineParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var station = entities.SpawnEntity("ExodusStructureStockStation", MapCoordinates.Nullspace);
            entities.System<StationSystem>().SetStation(station, station);
            var shield = entities.SpawnEntity("ShieldGeneratorCdm", map.MapCoords);
            try
            {
                var machine = entities.GetComponent<MachineComponent>(shield);
                Assert.Multiple(() =>
                {
                    Assert.That(machine.BoardContainer.ContainedEntities, Has.Count.EqualTo(1));
                    Assert.That(machine.PartContainer.ContainedEntities, Is.Not.Empty,
                        "The stocked shield must actually contain its construction parts.");
                });

                var sold = new EntitySoldEvent([shield], station);
                entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

                var stock = inventory.GetStock();
                Assert.That(stock, Has.Count.EqualTo(1), "The machine and its parts must not become separate stock.");
                Assert.Multiple(() =>
                {
                    Assert.That(stock[0].Prototype.Id, Is.EqualTo("ShieldGeneratorCdm"));
                    Assert.That(stock[0].Quantity, Is.EqualTo(1));
                    Assert.That(stock[0].StackPrototype, Is.Null);
                });
            }
            finally
            {
                entities.DeleteEntity(shield);
                entities.DeleteEntity(station);
                inventory.Clear();
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StructureStockRulePreservesRestrictedComponentBlacklist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var station = entities.SpawnEntity("ExodusStructureStockStation", MapCoordinates.Nullspace);
            entities.System<StationSystem>().SetStation(station, station);
            var allowed = entities.SpawnEntity("ExodusStructureStockAllowed", MapCoordinates.Nullspace);
            var gun = entities.SpawnEntity("ExodusStructureStockGun", MapCoordinates.Nullspace);
            var storage = entities.SpawnEntity("ExodusStructureStockStorage", MapCoordinates.Nullspace);
            var contraband = entities.SpawnEntity("ExodusStructureStockContraband", MapCoordinates.Nullspace);
            try
            {
                var sold = new EntitySoldEvent([allowed, gun, storage, contraband], station);
                entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

                var stock = inventory.GetStock();
                Assert.That(stock, Has.Count.EqualTo(1));
                Assert.Multiple(() =>
                {
                    Assert.That(stock[0].Prototype.Id, Is.EqualTo("ExodusStructureStockAllowed"));
                    Assert.That(stock[0].Quantity, Is.EqualTo(1));
                });
            }
            finally
            {
                entities.DeleteEntity(allowed);
                entities.DeleteEntity(gun);
                entities.DeleteEntity(storage);
                entities.DeleteEntity(contraband);
                entities.DeleteEntity(station);
                inventory.Clear();
            }
        });

        await pair.CleanReturnAsync();
    }
}
