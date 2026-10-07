using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Cargo.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketPurchaseTest
{
    [Test]
    public async Task GeneratedReceiptsDoNotCreateResaleValue()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var receipt = entities.SpawnEntity("PaperCargoInvoice", MapCoordinates.Nullspace);
            var stationery = entities.SpawnEntity("PaperCargoInvoice", MapCoordinates.Nullspace);
            var pricing = entities.System<PricingSystem>();
            Assert.That(pricing.GetPrice(stationery), Is.GreaterThan(0));
            entities.AddComponent<MarketReceiptComponent>(receipt);
            Assert.That(pricing.GetPrice(receipt), Is.Zero);
            Assert.That(entities.System<MarketBasketSystem>().TryGetEntityBasket(receipt, out var basket, out _), Is.True);
            Assert.That(basket.NominalValue, Is.Zero);
            Assert.That(pricing.GetPrice(stationery), Is.GreaterThan(0));
            entities.DeleteEntity(receipt);
            entities.DeleteEntity(stationery);
        });
        await pair.CleanReturnAsync();
    }

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusPurchaseTestConsole
  components:
  - type: CargoOrderConsole
  - type: StationTracker

- type: entity
  id: ExodusPurchaseTestItem
  components:
  - type: StaticPrice
    price: 150

- type: cargoProduct
  id: ExodusPurchaseTestProduct
  icon:
    sprite: Objects/Materials/Sheets/metal.rsi
    state: steel
  product: ExodusPurchaseTestItem
  cost: 150
  category: cargoproduct-category-name-materials
  group: market
";

    [Test]
    public async Task CargoPurchaseCannotUndercutRemoteResale()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var market = server.System<DynamicMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var orders = entities.AddComponent<StationCargoOrderDatabaseComponent>(station);
            var console = entities.SpawnEntity("ExodusPurchaseTestConsole", MapCoordinates.Nullspace);
            entities.System<StationSystem>().SetStation(console, station);
            var actor = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<BankAccountComponent>(actor);
            var key = DynamicMarketSystem.ProtoKey("ExodusPurchaseTestItem");
            market.SetFactor(key, 1);

            var request = new CargoConsoleAddOrderMessage("Tester", "", "ExodusPurchaseTestProduct", 1)
            {
                Actor = actor,
            };
            entities.EventBus.RaiseLocalEvent(console, request);

            Assert.Multiple(() =>
            {
                Assert.That(orders.Orders, Has.Count.EqualTo(1));
                // 150 * integral(exp(0.0008 u), 0..1) * 1.5 * 1.05 = 236.3445...
                Assert.That(orders.Orders[0].TotalPrice, Is.GreaterThanOrEqualTo(237),
                    "Cargo must not sell below the global resale ceiling including its own buy pressure.");
                Assert.That(market.GetFactor(key), Is.EqualTo(1), "A quote cannot change market factors.");
            });

            var displayedPrice = orders.Orders[0].TotalPrice;
            market.SetFactor(key, 2);
            entities.EventBus.RaiseLocalEvent(console, new CargoConsoleApproveOrderMessage(orders.Orders[0].OrderId, displayedPrice)
            {
                Actor = actor,
            });
            Assert.Multiple(() =>
            {
                Assert.That(orders.Orders[0].Approved, Is.False);
                Assert.That(orders.Orders[0].TotalPrice, Is.GreaterThan(displayedPrice),
                    "A stale approval must publish a fresh quote before trying to charge the buyer.");
                Assert.That(market.GetFactor(key), Is.EqualTo(2));
            });

            market.ResetKey(key);
            entities.DeleteEntity(actor);
            entities.DeleteEntity(console);
            entities.DeleteEntity(station);
        });

        await pair.CleanReturnAsync();
    }
}
