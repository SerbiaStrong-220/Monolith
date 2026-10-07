// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Systems;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Cargo.Events;
using Content.Shared.Materials;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketMaterialStockTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: material
  id: ExodusMarketStockMaterial
  icon:
    sprite: Objects/Materials/Sheets/metal.rsi
    state: steel
  price: 2
  stackEntity: ExodusMarketStockSheet

- type: stack
  id: ExodusMarketStockStack
  name: stack-steel
  spawn: ExodusMarketStockSheet
  maxCount: 50

- type: entity
  id: ExodusMarketStockSheet
  components:
  - type: Material
  - type: Stack
    stackType: ExodusMarketStockStack
    count: 1
  - type: PhysicalComposition
    materialComposition:
      ExodusMarketStockMaterial: 100

- type: material
  id: ExodusMarketZeroVolumeMaterial
  icon:
    sprite: Objects/Materials/Sheets/metal.rsi
    state: steel
  price: 2
  stackEntity: ExodusMarketZeroVolumeSheet

- type: stack
  id: ExodusMarketZeroVolumeStack
  name: stack-steel
  spawn: ExodusMarketZeroVolumeSheet
  maxCount: 50

- type: entity
  id: ExodusMarketZeroVolumeSheet
  components:
  - type: Material
  - type: Stack
    stackType: ExodusMarketZeroVolumeStack
    count: 1
  - type: PhysicalComposition
    materialComposition:
      ExodusMarketZeroVolumeMaterial: 0

- type: material
  id: ExodusMarketMissingVolumeMaterial
  icon:
    sprite: Objects/Materials/Sheets/metal.rsi
    state: steel
  price: 2
  stackEntity: ExodusMarketMissingVolumeSheet

- type: stack
  id: ExodusMarketMissingVolumeStack
  name: stack-steel
  spawn: ExodusMarketMissingVolumeSheet
  maxCount: 50

- type: entity
  id: ExodusMarketMissingVolumeSheet
  components:
  - type: Material
  - type: Stack
    stackType: ExodusMarketMissingVolumeStack
    count: 1
  - type: PhysicalComposition
";

    [Test]
    public async Task StoredMaterialsBecomeStackUnitsAndKeepTheirRemainder()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var station = CreateMarket(entities);
            var storage = CreateStorage(entities, "ExodusMarketStockMaterial");
            var sold = new EntitySoldEvent([storage], station);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

            var stock = inventory.GetStock();
            Assert.Multiple(() =>
            {
                Assert.That(stock, Has.Count.EqualTo(1));
                Assert.That(stock[0].Prototype.Id, Is.EqualTo("ExodusMarketStockSheet"));
                Assert.That(stock[0].StackPrototype?.Id, Is.EqualTo("ExodusMarketStockStack"));
                Assert.That(stock[0].Quantity, Is.EqualTo(2));
                Assert.That(stock[0].Price, Is.EqualTo(200));
                Assert.That(entities.System<MarketSystem>().GetAmountPerEntitySpace(stock[0]), Is.EqualTo(50));
                Assert.That(entities.System<MarketSystem>().CalculateEntityAmount(stock), Is.EqualTo(1));
                Assert.That(entities.System<SharedMaterialStorageSystem>().GetMaterialAmount(storage, "ExodusMarketStockMaterial"),
                    Is.EqualTo(50));
            });

            entities.DeleteEntity(storage);
            entities.DeleteEntity(station);
            inventory.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RejectedStockIncreaseDoesNotConsumeStoredMaterials()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var station = CreateMarket(entities);
            Assert.That(inventory.TryAddStock("ExodusMarketStockSheet", int.MaxValue, 300, "ExodusMarketStockStack"), Is.True);
            var storage = CreateStorage(entities, "ExodusMarketStockMaterial");
            var sold = new EntitySoldEvent([storage], station);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

            Assert.Multiple(() =>
            {
                Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(int.MaxValue));
                Assert.That(inventory.GetStock()[0].Price, Is.EqualTo(300));
                Assert.That(entities.System<SharedMaterialStorageSystem>().GetMaterialAmount(storage, "ExodusMarketStockMaterial"),
                    Is.EqualTo(250));
            });

            entities.DeleteEntity(storage);
            entities.DeleteEntity(station);
            inventory.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("ExodusMarketZeroVolumeMaterial")]
    [TestCase("ExodusMarketMissingVolumeMaterial")]
    public async Task InvalidPerUnitCompositionDoesNotConsumeMaterials(string material)
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var station = CreateMarket(entities);
            var storage = CreateStorage(entities, material);
            var sold = new EntitySoldEvent([storage], station);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

            Assert.Multiple(() =>
            {
                Assert.That(inventory.GetStock(), Is.Empty);
                Assert.That(entities.System<SharedMaterialStorageSystem>().GetMaterialAmount(storage, material), Is.EqualTo(250));
            });

            entities.DeleteEntity(storage);
            entities.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid CreateMarket(IEntityManager entities)
    {
        var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
        entities.AddComponent<StationTrackerComponent>(station);
        entities.System<StationSystem>().SetStation(station, station);
        entities.AddComponent<CargoMarketDataComponent>(station);
        return station;
    }

    private static EntityUid CreateStorage(IEntityManager entities, string material)
    {
        var storage = entities.SpawnEntity(null, MapCoordinates.Nullspace);
        entities.AddComponent<MaterialStorageComponent>(storage);
        Assert.That(entities.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(storage, material, 250), Is.True);
        return storage;
    }
}
