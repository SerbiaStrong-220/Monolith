using Content.Server._Exodus.Economy;
using Content.Server._NF.Market.Components;
using Content.Server._NF.Market.Systems;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.Economy;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.Events;
using Content.Shared.Station.Components;
using Content.Shared.Whitelist;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class CargoMarketTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusCargoMarketTestConsole
  components:
  - type: CargoOrderConsole
  - type: StationTracker

- type: stack
  id: ExodusCargoMarketTestStack
  name: stack-steel
  spawn: ExodusCargoMarketTestItem
  maxCount: 50

- type: entity
  id: ExodusCargoMarketTestItem
  components:
  - type: Stack
    stackType: ExodusCargoMarketTestStack
    count: 50

- type: cargoProduct
  id: ExodusCargoMarketTestProduct
  icon:
    sprite: Objects/Materials/Sheets/metal.rsi
    state: steel
  product: ExodusCargoMarketTestItem
  cost: 100
  category: cargoproduct-category-name-materials
  group: market
";

    [Test]
    public async Task StockSoldAtOneStationIsAvailableAtAnother()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var firstStation = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<CargoMarketDataComponent>(firstStation);
            entities.AddComponent<StationTrackerComponent>(firstStation);
            entities.System<StationSystem>().SetStation(firstStation, firstStation);
            var secondStation = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<CargoMarketDataComponent>(secondStation);
            var item = entities.SpawnEntity("ExodusCargoMarketTestItem", MapCoordinates.Nullspace);
            var sold = new EntitySoldEvent([item], firstStation);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

            Assert.That(entities.System<MarketSystem>().TryGetStock(secondStation, "ExodusCargoMarketTestItem", out var stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(50));

            entities.DeleteEntity(item);
            entities.DeleteEntity(firstStation);
            Assert.That(entities.System<MarketSystem>().TryGetStock(secondStation, "ExodusCargoMarketTestItem", out stock), Is.True);
            Assert.That(stock!.Quantity, Is.EqualTo(50), "Removing the selling station must not delete sector stock.");
            entities.DeleteEntity(secondStation);
            inventory.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MissingWhitelistOverrideDoesNotBypassStockBlacklist()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<StationTrackerComponent>(station);
            entities.System<StationSystem>().SetStation(station, station);
            var stock = entities.AddComponent<CargoMarketDataComponent>(station);
#pragma warning disable RA0002 // Configure stock restrictions for the regression fixture.
            stock.Blacklist = new EntityWhitelist { Components = ["Stack"] };
#pragma warning restore RA0002
            var item = entities.SpawnEntity("ExodusCargoMarketTestItem", MapCoordinates.Nullspace);

            var sold = new EntitySoldEvent([item], station);
            entities.EventBus.RaiseEvent(EventSource.Local, ref sold);

            Assert.That(inventory.GetStock(), Is.Empty);
            entities.DeleteEntity(item);
            entities.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReturningReservationRestoresQuantityAndBasePrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.ResolveDependency<IEntityManager>();

        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<CargoMarketDataComponent>(station);
            Assert.That(inventory.TryAddStock("ExodusCargoMarketTestItem", 4, 0.25, "ExodusCargoMarketTestStack"), Is.True);
            var market = entities.System<MarketSystem>();

            Assert.That(market.TryTakeStock(station, "ExodusCargoMarketTestItem", 4, out var reservation), Is.True);
            Assert.That(inventory.GetStock(), Is.Empty);
            Assert.That(market.TryTakeStock(station, "ExodusCargoMarketTestItem", 1, out _), Is.False);
            market.ReturnStock(station, reservation);

            var stock = inventory.GetStock();
            Assert.Multiple(() =>
            {
                Assert.That(stock, Has.Count.EqualTo(1));
                Assert.That(stock[0].Quantity, Is.EqualTo(4));
                Assert.That(stock[0].Price, Is.EqualTo(0.25));
                Assert.That(stock[0].StackPrototype?.Id, Is.EqualTo("ExodusCargoMarketTestStack"));
            });
            entities.DeleteEntity(station);
            inventory.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ResaleOrderRetainsFractionalUnitPrice()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var orders = entities.AddComponent<StationCargoOrderDatabaseComponent>(station);
            entities.AddComponent<CargoMarketDataComponent>(station);
            Assert.That(inventory.TryAddStock("ExodusCargoMarketTestItem", 4, 0.25, "ExodusCargoMarketTestStack"), Is.True);
            var console = entities.SpawnEntity("ExodusCargoMarketTestConsole", MapCoordinates.Nullspace);
            entities.System<StationSystem>().SetStation(console, station);
            var actor = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<BankAccountComponent>(actor);

            var request = new CargoConsoleAddOrderMessage("Tester", "", CargoMarketListing.MakeResaleProductId("ExodusCargoMarketTestItem"), 4)
            {
                Actor = actor,
            };
            entities.EventBus.RaiseLocalEvent(console, request);

            Assert.Multiple(() =>
            {
                Assert.That(orders.Orders, Has.Count.EqualTo(1));
                Assert.That(orders.Orders[0].Price, Is.EqualTo(0.25));
                Assert.That(orders.Orders[0].TotalPrice, Is.EqualTo(2));
                Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(4), "Requesting a quote must not reserve stock.");
            });

            entities.DeleteEntity(console);
            entities.DeleteEntity(actor);
            entities.DeleteEntity(station);
            inventory.Clear();
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancellingOrdersPreservesApprovedDeliveryAndStock(bool approved)
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            Assert.That(inventory.TryAddStock("ExodusCargoMarketTestItem", 4, 0.25, "ExodusCargoMarketTestStack"), Is.True);
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<CargoMarketDataComponent>(station);
            var orders = entities.AddComponent<StationCargoOrderDatabaseComponent>(station);
            var order = new CargoOrderData(1, "ExodusCargoMarketTestItem", "Test stock", 0.25, 2, "Tester", "", null, true)
            {
                Approved = approved,
                TotalPrice = 2,
            };
            orders.Orders.Add(order);
            if (approved)
                Assert.That(entities.System<MarketSystem>().TryTakeStock(station, "ExodusCargoMarketTestItem", 2, out _), Is.True);

            var cargo = entities.System<CargoSystem>();
            cargo.RemoveOrder(station, order.OrderId, orders);
            cargo.RemoveOrder(station, order.OrderId, orders);

            Assert.Multiple(() =>
            {
                Assert.That(orders.Orders, Has.Count.EqualTo(approved ? 1 : 0));
                Assert.That(inventory.GetStock()[0].Quantity, Is.EqualTo(approved ? 2 : 4));
                Assert.That(order.NumDispatched, Is.Zero);
                Assert.That(order.Approved, Is.EqualTo(approved));
            });
            entities.DeleteEntity(station);
            inventory.Clear();
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CatalogQuoteIncludesAllUnitsInSpawnedStack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var market = server.System<DynamicMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var orders = entities.AddComponent<StationCargoOrderDatabaseComponent>(station);
            var console = entities.SpawnEntity("ExodusCargoMarketTestConsole", MapCoordinates.Nullspace);
            entities.System<StationSystem>().SetStation(console, station);
            var actor = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<BankAccountComponent>(actor);
            var key = DynamicMarketSystem.StackKey("ExodusCargoMarketTestStack");
            market.SetFactor(key, 1);

            var request = new CargoConsoleAddOrderMessage("Tester", "", "ExodusCargoMarketTestProduct", 1)
            {
                Actor = actor,
            };
            entities.EventBus.RaiseLocalEvent(console, request);

            Assert.Multiple(() =>
            {
                Assert.That(orders.Orders, Has.Count.EqualTo(1));
                Assert.That(orders.Orders[0].TotalPrice, Is.EqualTo(103));
                Assert.That(market.GetFactor(key), Is.EqualTo(1), "Quoting must not change global prices.");
            });

            market.ResetKey(key);
            entities.DeleteEntity(console);
            entities.DeleteEntity(actor);
            entities.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }
}
