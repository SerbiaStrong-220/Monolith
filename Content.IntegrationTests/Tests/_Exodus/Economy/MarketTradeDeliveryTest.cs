// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._NF.Trade;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.CCVar;
using Content.Shared._NF.Trade;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Station.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketTradeDeliveryTest
{
    [TestCase("CrateTradeSecureNormal", false, 750, 5750)]
    [TestCase("CrateTradeSecureNormal", true, 750, 5750)]
    [TestCase("CrateTradeSecureHigh", false, 1500, 9000)]
    [TestCase("CrateTradeSecureHigh", true, 1500, 9000)]
    public async Task DestinationDeliveryEarnsProfitButImmediateResaleDoesNot(
        string prototype, bool enabled, int elsewherePrice, int destinationPrice)
    {
        await RunDeliveryTest(prototype, enabled, (entities, crate, destination, product) =>
        {
            var purchase = QuotePurchase(entities, product);
            entities.System<DynamicMarketSystem>().CommitTransaction(purchase.Transaction);

            Assert.That(entities.System<PricingSystem>().GetPrice(crate), Is.EqualTo(elsewherePrice));
            Assert.That(AppraiseSale(entities, crate) * 1.3, Is.LessThan(purchase.TotalPrice),
                "Immediate resale, including the crate's 30% Frontier credit, must not recover the purchase price.");

            entities.System<StationSystem>().SetStation(crate, destination);
            Assert.That(entities.System<PricingSystem>().GetPrice(crate), Is.EqualTo(destinationPrice));
            Assert.That(AppraiseSale(entities, crate), Is.GreaterThan(purchase.TotalPrice),
                "Completing the delivery must remain profitable with the real sale endpoint ceilings.");
        });
    }

    [TestCase("CrateTradeSecureNormal", 3250)]
    [TestCase("CrateTradeSecureHigh", 2000)]
    public async Task LateDeliveryKeepsItsPenalty(string prototype, int lateDestinationPrice)
    {
        await RunDeliveryTest(prototype, true, (entities, crate, destination, _) =>
        {
            var trade = entities.GetComponent<TradeCrateComponent>(crate);
#pragma warning disable RA0002 // Advance only this fixture's deadline instead of waiting through the delivery window.
            trade.ExpressDeliveryTime = TimeSpan.FromSeconds(-1);
#pragma warning restore RA0002
            Assert.That(entities.System<PricingSystem>().GetPrice(crate), Is.Zero,
                "An expired undelivered crate must retain its non-negative, penalized value.");

            entities.System<StationSystem>().SetStation(crate, destination);
            Assert.That(entities.System<PricingSystem>().GetPrice(crate), Is.EqualTo(lateDestinationPrice));
            Assert.That(entities.System<MarketBasketSystem>().TryGetEntityBasket(crate, out var basket, out var failure),
                Is.True, failure);
            Assert.That(basket.NominalValue, Is.EqualTo(lateDestinationPrice),
                "Live sale appraisal must not reuse the prototype's non-delivery purchase bound.");
        });
    }

    [TestCase("CrateTradeSecureNormal", 11500)]
    [TestCase("CrateTradeSecureHigh", 18000)]
    public async Task WildcardPremiumRewardsDeliveryWithoutRaisingThePurchasePrice(string prototype, int wildcardPrice)
    {
        await RunDeliveryTest(prototype, true, (entities, crate, _, product) =>
        {
            var purchase = QuotePurchase(entities, product);
            var destination = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                var wildcard = entities.AddComponent<TradeCrateWildcardDestinationComponent>(destination);
                wildcard.ValueMultiplier = 2;
                Assert.That(QuotePurchase(entities, product).TotalPrice, Is.EqualTo(purchase.TotalPrice),
                    "A wildcard destination must not capitalize its delivery reward into the purchase floor.");

                entities.System<DynamicMarketSystem>().CommitTransaction(purchase.Transaction);
                entities.System<StationSystem>().SetStation(crate, destination);
                Assert.That(entities.System<PricingSystem>().GetPrice(crate), Is.EqualTo(wildcardPrice));
                Assert.That(AppraiseSale(entities, crate), Is.GreaterThan(purchase.TotalPrice));
            }
            finally
            {
                entities.DeleteEntity(destination);
            }
        });
    }

    private static MarketPurchaseQuote QuotePurchase(IEntityManager entities, CargoProductPrototype product)
    {
        Assert.That(entities.System<MarketPurchaseSystem>().TryQuotePrototypeBuy(
            product.Product, 1, product.Cost, 1, out var purchase), Is.True);
        return purchase!;
    }

    private static int AppraiseSale(IEntityManager entities, EntityUid crate)
    {
        Assert.That(entities.System<MarketBasketSystem>().TryGetEntityBasket(crate, out var basket, out var failure),
            Is.True, failure);
        var market = entities.System<DynamicMarketSystem>();
        var transaction = new MarketTransactionState();
        double total = 0;
        foreach (var line in basket.Lines)
        {
            total += market.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice, line.Quantity,
                1, 1, transaction, applyImpact: false);
        }

        return DynamicMarketSystem.RoundSellPayout(total);
    }

    private static async Task RunDeliveryTest(string prototype, bool marketEnabled,
        Action<IEntityManager, EntityUid, EntityUid, CargoProductPrototype> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.ResolveDependency<IEntityManager>();
            var configuration = server.ResolveDependency<IConfigurationManager>();
            var market = entities.System<DynamicMarketSystem>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
            var definitions = new[]
            {
                EXCVars.DynamicMarketMinFactor,
                EXCVars.DynamicMarketMaxFactor,
                EXCVars.DynamicMarketSellImpact,
                EXCVars.DynamicMarketBuyImpact,
                EXCVars.DynamicMarketReferenceVolume,
                EXCVars.DynamicMarketDecayRate,
                EXCVars.MarketPurchaseMargin,
            };
            var previous = new float[definitions.Length];
            for (var i = 0; i < definitions.Length; i++)
                previous[i] = configuration.GetCVar(definitions[i]);

            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, marketEnabled);
            for (var i = 0; i < definitions.Length; i++)
                configuration.SetCVar(definitions[i], definitions[i].DefaultValue);
            market.ResetAll();

            var source = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var destination = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var crate = entities.SpawnEntity(prototype, MapCoordinates.Nullspace);
            try
            {
                entities.AddComponent<StationTrackerComponent>(crate);
                entities.System<StationSystem>().SetStation(crate, source);
                var trade = entities.GetComponent<TradeCrateComponent>(crate);
#pragma warning disable RA0002 // Configure a deterministic route and deadline for the real runtime price handler.
                trade.DestinationStation = destination;
                trade.ExpressDeliveryTime = server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromMinutes(1);
#pragma warning restore RA0002
                var product = server.ResolveDependency<IPrototypeManager>().Index<CargoProductPrototype>(prototype);
                assertion(entities, crate, destination, product);
            }
            finally
            {
                entities.DeleteEntity(crate);
                entities.DeleteEntity(destination);
                entities.DeleteEntity(source);
                market.ResetAll();
                for (var i = 0; i < definitions.Length; i++)
                    configuration.SetCVar(definitions[i], previous[i]);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
