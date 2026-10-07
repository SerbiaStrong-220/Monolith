// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Shared._Crescent.Dispenser;
using Content.Shared.Cargo.Components;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketFixedPayoutTest
{
    private static readonly EntProtoId DepotChute = "CargoChuteDepot";
    private static readonly EntProtoId JupiterChute = "CargoChuteJupiter";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusFixedPayoutBundle
          components:
          - type: StaticPrice
            price: 100
          - type: ContainerContainer
            containers:
              cargo: !type:Container
          - type: ContainerFill
            containers:
              cargo:
              - TradeGoodSupplies
              - TradeGoodSupplies

        - type: entity
          id: ExodusFixedPayoutPackage
          components:
          - type: SpawnItemsOnUse
            items:
            - id: TradeGoodSupplies
              amount: 2

        - type: entity
          id: ExodusFixedPayoutNestedBundle
          parent: ExodusFixedPayoutBundle
          components:
          - type: ContainerFill
            containers:
              cargo:
              - ExodusFixedPayoutPackage

        - type: entity
          id: ExodusFixedPayoutReusablePackage
          parent: ExodusFixedPayoutPackage
          components:
          - type: SpawnItemsOnUse
            uses: 2
            items:
            - id: TradeGoodSupplies
              amount: 2
        """;

    [Test]
    public async Task UnspawnedCashChutesProtectDepressedTradeGoodsPurchase()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.EntMan;
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        var factory = pair.Server.ResolveDependency<IComponentFactory>();
        await pair.Server.WaitAssertion(() =>
        {
            var depot = prototypes.Index(DepotChute);
            Assert.That(depot.TryGetComponent<MarketStockSourceComponent>(out _, factory), Is.True);
            Assert.That(depot.TryGetComponent<DispenserComponent>(out var dispenser, factory), Is.True);
            var reward = prototypes.Index<EntityPrototype>(dispenser!.Inventory["TradeGoodSupplies"]);
            Assert.That(reward.TryGetComponent<CashComponent>(out _, factory), Is.True);
            Assert.That(reward.TryGetComponent<StackComponent>(out var cash, factory), Is.True);
            Assert.That(cash!.Count, Is.EqualTo(3000), "The depot pays a fixed 3000 credits for a supplies crate.");
            var jupiter = prototypes.Index(JupiterChute);
            Assert.That(jupiter.TryGetComponent<DispenserComponent>(out var remote, factory), Is.True);
            var remoteReward = prototypes.Index<EntityPrototype>(remote!.Inventory["TradeGoodSupplies"]);
            Assert.That(remoteReward.TryGetComponent<StackComponent>(out var remoteCash, factory), Is.True);
            Assert.That(remoteCash!.Count, Is.EqualTo(8000));

            var ceilings = entities.System<MarketSellCeilingSystem>();
            var market = entities.System<DynamicMarketSystem>();
            var purchases = entities.System<MarketPurchaseSystem>();
            var key = DynamicMarketSystem.ProtoKey("TradeGoodSupplies");
            var previous = market.GetFactor(key);
            try
            {
                var ceiling = ceilings.GetSnapshot();
                Assert.That(ceiling.FixedCreditPayouts![key], Is.GreaterThanOrEqualTo(8000),
                    "A future map's higher fixed cash reward must be covered before that endpoint spawns.");
                market.SetFactor(key, 0.01);
                Assert.That(purchases.TryQuotePrototypeBuy("TradeGoodSupplies", 1, 100, 1, out var quote), Is.True);
                var margin = entities.System<MarketSettingsSystem>().Current.PurchaseMargin;
                Assert.That(quote!.TotalPrice, Is.GreaterThanOrEqualTo(Math.Ceiling(8000 * (1 + margin))),
                    "A fixed payout must not be discounted by the dynamic market factor.");
                Assert.That(market.GetFactor(key), Is.EqualTo(0.01), "Calculating the floor must not commit demand.");
            }
            finally
            {
                market.SetFactor(key, previous);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LiveFixedPayoutEditsAndEitherComponentOrderUpdateTheBound(bool markerFirst)
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var ceilings = entities.System<MarketSellCeilingSystem>();
            var key = DynamicMarketSystem.ProtoKey("TradeGoodSupplies");
            var baseline = ceilings.GetSnapshot().FixedCreditPayouts![key];
            var endpoint = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                if (markerFirst)
                    entities.AddComponent<MarketStockSourceComponent>(endpoint);
                var dispenser = entities.AddComponent<DispenserComponent>(endpoint);
                dispenser.Inventory["TradeGoodSupplies"] = "SpaceCash50000";
                // A free default interaction is not payment for any particular delivered item.
                dispenser.DefaultItem = "SpaceCash1000000";
                if (!markerFirst)
                    entities.AddComponent<MarketStockSourceComponent>(endpoint);
                var frozen = ceilings.GetSnapshot();
                Assert.That(frozen.FixedCreditPayouts![key], Is.EqualTo(50000));
                dispenser.Inventory["TradeGoodSupplies"] = "SpaceCash24000";
                Assert.That(ceilings.GetSnapshot().FixedCreditPayouts![key], Is.EqualTo(24000));
                Assert.That(frozen.FixedCreditPayouts[key], Is.EqualTo(50000),
                    "A captured quote snapshot must remain detached from later VV edits.");
                dispenser.Inventory["TradeGoodSupplies"] = "PaperWrittenDispenserOutageNote";
                Assert.That(ceilings.GetSnapshot().FixedCreditPayouts![key], Is.EqualTo(baseline),
                    "A non-cash output must not become a credit payout floor.");
                dispenser.Inventory["TradeGoodSupplies"] = "SpaceCash50000";
                entities.RemoveComponent<MarketStockSourceComponent>(endpoint);
                Assert.That(ceilings.GetSnapshot().FixedCreditPayouts![key], Is.EqualTo(baseline));
                entities.AddComponent<MarketStockSourceComponent>(endpoint);
                Assert.That(ceilings.GetSnapshot().FixedCreditPayouts![key], Is.EqualTo(50000));
                entities.RemoveComponent<DispenserComponent>(endpoint);
                Assert.That(ceilings.GetSnapshot().FixedCreditPayouts![key], Is.EqualTo(baseline));
            }
            finally
            {
                entities.DeleteEntity(endpoint);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NestedGoodsKeepFixedPayoutFloorAndPurchaseRevenueShare()
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var market = entities.System<DynamicMarketSystem>();
            var purchases = entities.System<MarketPurchaseSystem>();
            var key = DynamicMarketSystem.ProtoKey("TradeGoodSupplies");
            var wrapperKey = DynamicMarketSystem.ProtoKey("ExodusFixedPayoutBundle");
            var previous = market.GetFactor(key);
            var previousWrapper = market.GetFactor(wrapperKey);
            try
            {
                market.SetFactor(key, 0.01);
                market.SetFactor(wrapperKey, 0.01);
                const double returnRate = 0.75;
                Assert.That(purchases.TryQuotePrototypeBuy("ExodusFixedPayoutBundle", 3, 100, 1, out var quote,
                    purchaseReturnRate: returnRate), Is.True);
                var margin = entities.System<MarketSettingsSystem>().Current.PurchaseMargin;
                var minimum = 6 * 8000 * (1 + margin) + quote!.NominalPrice * returnRate;
                Assert.That(quote.TotalPrice, Is.GreaterThanOrEqualTo(Math.Ceiling(minimum)),
                    "The buyer can unpack every supplies crate and recover their nominal revenue share separately.");
            }
            finally
            {
                market.SetFactor(key, previous);
                market.SetFactor(wrapperKey, previousWrapper);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("SpaceCash10000", 10000)]
    [TestCase("SpaceCash50000", 50000)]
    public async Task ExactUsePackageChargesMaximumOfIntactRewardOrUnpackedContents(string reward, int payout)
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var market = entities.System<DynamicMarketSystem>();
            var purchases = entities.System<MarketPurchaseSystem>();
            var key = DynamicMarketSystem.ProtoKey("TradeGoodSupplies");
            var previous = market.GetFactor(key);
            var endpoint = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                entities.AddComponent<MarketStockSourceComponent>(endpoint);
                var dispenser = entities.AddComponent<DispenserComponent>(endpoint);
                dispenser.Inventory["ExodusFixedPayoutPackage"] = reward;
                market.SetFactor(key, 0.01);
                Assert.That(purchases.TryQuotePurchase(
                    [new MarketPurchaseRequest("ExodusFixedPayoutPackage", 1, 100),
                     new MarketPurchaseRequest("TradeGoodSupplies", 1, 100)],
                    0, out var quote), Is.True);
                var margin = entities.System<MarketSettingsSystem>().Current.PurchaseMargin;
                var minimum = (Math.Max(payout, 2 * 8000) + 8000) * (1 + margin);
                Assert.That(quote!.TotalPrice, Is.EqualTo(Math.Ceiling(minimum)),
                    "Opening consumes the package: charge the larger whole/contents bound, then add independent basket goods.");
            }
            finally
            {
                entities.DeleteEntity(endpoint);
                market.SetFactor(key, previous);
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ExodusFixedPayoutNestedBundle", "ExodusFixedPayoutPackage", 50000)]
    [TestCase("ExodusFixedPayoutReusablePackage", "ExodusFixedPayoutReusablePackage", 66000)]
    public async Task NestedOrPartiallyOpenedPackagesRetainTheirFixedCashBound(string purchased, string exchanged, int minimumPayout)
    {
        await using var pair = await PoolManager.GetServerClient();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var market = entities.System<DynamicMarketSystem>();
            var purchases = entities.System<MarketPurchaseSystem>();
            var key = DynamicMarketSystem.ProtoKey("TradeGoodSupplies");
            var previous = market.GetFactor(key);
            var endpoint = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                entities.AddComponent<MarketStockSourceComponent>(endpoint);
                var dispenser = entities.AddComponent<DispenserComponent>(endpoint);
                dispenser.Inventory[exchanged] = "SpaceCash50000";
                market.SetFactor(key, 0.01);
                Assert.That(purchases.TryQuotePrototypeBuy(purchased, 1, 100, 1, out var quote), Is.True);
                var margin = entities.System<MarketSettingsSystem>().Current.PurchaseMargin;
                Assert.That(quote!.TotalPrice, Is.GreaterThanOrEqualTo(Math.Ceiling(minimumPayout * (1 + margin))),
                    "A nested wrapper retains its exchange value; a multi-use wrapper can pay after earlier uses have already released goods.");
                Assert.That(quote.TotalPrice, Is.LessThan(Math.Ceiling((minimumPayout + 16000) * (1 + margin))),
                    "The final payload and intact wrapper must not both be charged after the last use consumes the wrapper.");
            }
            finally
            {
                entities.DeleteEntity(endpoint);
                market.SetFactor(key, previous);
            }
        });
        await pair.CleanReturnAsync();
    }
}
