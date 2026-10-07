// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Shared._Exodus.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketPurchaseBoundaryTest
{
    private const string ItemPrototype = "ExodusPurchaseBoundaryItem";
    private const string TaxedItemPrototype = "ExodusPurchaseBoundaryTaxedItem";
    private const string StackPrototype = "ExodusPurchaseBoundaryStack";
    private const string StackKey = "stack:ExodusPurchaseBoundaryUnits";
    private static readonly MarketSellCeiling Ceiling = new(1.5, 1.5);

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusPurchaseBoundaryItem
          components:
          - type: Item
          - type: StaticPrice
            price: 150

        - type: entity
          id: ExodusPurchaseBoundaryTaxedItem
          parent: ExodusPurchaseBoundaryItem
          components:
          - type: ItemTax
            taxAccounts:
              Medical: 0.8

        - type: stack
          id: ExodusPurchaseBoundaryUnits
          name: stack-steel
          spawn: ExodusPurchaseBoundaryStack
          maxCount: 50

        - type: entity
          id: ExodusPurchaseBoundaryStack
          components:
          - type: Item
          - type: Stack
            stackType: ExodusPurchaseBoundaryUnits
            count: 50
          - type: StackPrice
            price: 150

        - type: entity
          id: ExodusPurchaseBoundarySmallStack
          parent: ExodusPurchaseBoundaryStack
          components:
          - type: Stack
            count: 25

        - type: entity
          id: ExodusPurchaseBoundaryRandomStack
          components:
          - type: RandomSpawner
            prototypes:
            - ExodusPurchaseBoundaryStack
            - ExodusPurchaseBoundarySmallStack

        - type: entity
          id: ExodusPurchaseBoundaryRandomPackage
          components:
          - type: Item
          - type: SpawnItemsOnUse
            items:
            - id: ExodusPurchaseBoundaryItem
              prob: 0.5

        - type: stack
          id: ExodusPurchaseBoundaryFixedPriceUnits
          name: stack-steel
          spawn: ExodusPurchaseBoundaryFixedPriceSingle
          maxCount: 50

        - type: entity
          id: ExodusPurchaseBoundaryFixedPriceStack
          parent: ExodusPurchaseBoundaryItem
          components:
          - type: Stack
            stackType: ExodusPurchaseBoundaryFixedPriceUnits
            count: 50

        - type: entity
          id: ExodusPurchaseBoundaryFixedPriceSingle
          parent: ExodusPurchaseBoundaryFixedPriceStack
          components:
          - type: Stack
            count: 1

        - type: entity
          id: ExodusPurchaseBoundaryTradeCrate
          components:
          - type: StaticPrice
            price: 10
          - type: TradeCrate
            valueAtDestination: 200
            valueElsewhere: 50
            expressDeliveryDuration: 60
            expressOnTimeBonus: 30
            expressLatePenalty: -100

        - type: entity
          id: ExodusPurchaseBoundaryNonExpressTradeCrate
          parent: ExodusPurchaseBoundaryTradeCrate
          components:
          - type: TradeCrate
            expressDeliveryDuration: 0

        - type: entity
          id: ExodusPurchaseBoundaryHighRewardTradeCrate
          parent: ExodusPurchaseBoundaryTradeCrate
          components:
          - type: TradeCrate
            valueAtDestination: 100000
            expressOnTimeBonus: 50000

        - type: entity
          id: ExodusPurchaseBoundaryLoadedTradeCrate
          parent: CrateTradeSecureNormal
          components:
          - type: ContainerContainer
            containers:
              paper_label: !type:ContainerSlot
              cargo: !type:Container
          - type: ContainerFill
            containers:
              cargo:
              - ExodusPurchaseBoundaryItem
        """;

    [TestCase(false, 1)]
    [TestCase(false, 100)]
    [TestCase(false, 1000)]
    [TestCase(true, 1)]
    [TestCase(true, 100)]
    [TestCase(true, 1000)]
    public async Task BothTradeDirectionsLoseMoneyForTheSameCommodity(bool stackUnits, int quantity)
    {
        await RunTest((purchases, market, _) =>
        {
            var prototype = stackUnits ? StackPrototype : ItemPrototype;
            var key = stackUnits ? StackKey : $"proto:{ItemPrototype}";
            var buy = Quote(purchases, prototype, quantity, stackUnits: stackUnits);

            Assert.That(market.GetFactor(key), Is.EqualTo(1), "A purchase quote must not change the global market.");
            Assert.That(buy.Transaction.Factors[key], Is.GreaterThan(1), "The quotation must include its future buy pressure.");
            market.CommitTransaction(buy.Transaction);
            var resale = market.CalculateSequentialSellValue(key, 150, quantity, 1, 1.5, null, true);

            Assert.Multiple(() =>
            {
                Assert.That(buy.TotalPrice, Is.GreaterThanOrEqualTo(resale * 1.05 - 0.000001));
                Assert.That(market.GetFactor(key), Is.EqualTo(1).Within(0.000001));
            });

            market.SetFactor(key, 1);
            var initialSale = market.CalculateSequentialSellValue(key, 150, quantity, 1, 1.5, null, true);
            var factorAfterSale = market.GetFactor(key);
            var replacement = Quote(purchases, prototype, quantity, stackUnits: stackUnits);
            Assert.That(market.GetFactor(key), Is.EqualTo(factorAfterSale), "A quote cannot commit its hypothetical reverse sale either.");
            market.CommitTransaction(replacement.Transaction);

            Assert.Multiple(() =>
            {
                Assert.That(replacement.TotalPrice, Is.GreaterThanOrEqualTo(initialSale * 1.05 - 0.000001));
                Assert.That(market.GetFactor(key), Is.EqualTo(1).Within(0.000001));
            });
        });
    }

    [Test]
    public async Task SplittingBothPurchasesAndSalesCannotRecoverTheMargin()
    {
        await RunTest((purchases, market, _) =>
        {
            const int units = 1000;
            var bulkPurchase = Quote(purchases, StackPrototype, units, stackUnits: true);
            market.CommitTransaction(bulkPurchase.Transaction);
            var wholeSale = market.CalculateSequentialSellValue(StackKey, 150, units, 50, 1.5, null, false);
            double splitSale = 0;
            var splitPayout = 0;
            for (var i = 0; i < units; i++)
            {
                var value = market.CalculateSequentialSellValue(StackKey, 150, 1, 1, 1.5, null, true);
                splitSale += value;
                splitPayout += DynamicMarketSystem.RoundSellPayout(value);
            }

            Assert.Multiple(() =>
            {
                Assert.That(splitSale, Is.EqualTo(wholeSale).Within(0.000001));
                Assert.That(bulkPurchase.TotalPrice, Is.GreaterThan(splitPayout));
                Assert.That(bulkPurchase.TotalPrice, Is.GreaterThanOrEqualTo(splitSale * 1.05 - 0.000001));
                Assert.That(market.GetFactor(StackKey), Is.EqualTo(1).Within(0.000001));
            });

            market.SetFactor(StackKey, 1);
            var splitCharge = 0;
            for (var i = 0; i < units; i++)
            {
                var purchase = Quote(purchases, StackPrototype, 1, stackUnits: true);
                splitCharge += purchase.TotalPrice;
                market.CommitTransaction(purchase.Transaction);
            }

            var resale = market.CalculateSequentialSellValue(StackKey, 150, units, 50, 1.5, null, true);
            Assert.That(splitCharge, Is.GreaterThanOrEqualTo(resale * 1.05 - 0.000001));
            Assert.That(market.GetFactor(StackKey), Is.EqualTo(1).Within(0.000001));
        });
    }

    [Test]
    public async Task SplittingAStackWithAFixedEntityPriceCannotIncreaseLiquidationAboveTheFloor()
    {
        await RunTest((purchases, market, _) =>
        {
            const string key = "stack:ExodusPurchaseBoundaryFixedPriceUnits";
            var purchase = Quote(purchases, "ExodusPurchaseBoundaryFixedPriceStack", 1);
            market.CommitTransaction(purchase.Transaction);
            double liquidation = 0;
            // StackSystem.Split creates the stack prototype's canonical entity for each split.
            // Its StaticPrice remains 150 even when that entity contains only one unit.
            for (var i = 0; i < 50; i++)
                liquidation += market.CalculateSequentialSellValue(key, 150, 1, 1, 1.5, null, true);

            Assert.That(purchase.TotalPrice, Is.GreaterThanOrEqualTo(liquidation * 1.05 - 0.000001),
                "Entity-level static or reagent value must not be divided away when the purchased stack can be split.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FactorClampsDoNotRemoveThePurchaseMargin(bool startAtMaximum)
    {
        await RunTest((purchases, market, configuration) =>
        {
            const int quantity = 10_000;
            var startingFactor = configuration.GetCVar(startAtMaximum
                ? EXCVars.DynamicMarketMaxFactor
                : EXCVars.DynamicMarketMinFactor);
            market.SetFactor(StackKey, startingFactor);
            var purchase = Quote(purchases, StackPrototype, quantity, stackUnits: true);
            market.CommitTransaction(purchase.Transaction);
            var resale = market.CalculateSequentialSellValue(StackKey, 150, quantity, 1, 1.5, null, true);
            Assert.That(purchase.TotalPrice, Is.GreaterThanOrEqualTo(resale * 1.05 - 0.000001));

            market.SetFactor(StackKey, startingFactor);
            var saleFirst = market.CalculateSequentialSellValue(StackKey, 150, quantity, 1, 1.5, null, true);
            var replacement = Quote(purchases, StackPrototype, quantity, stackUnits: true);
            Assert.That(replacement.TotalPrice, Is.GreaterThanOrEqualTo(saleFirst * 1.05 - 0.000001));
        });
    }

    [TestCase(0.01f, 0.08f)]
    [TestCase(0.08f, 0.01f)]
    public async Task UnequalConfiguredImpactsUseOneReversibleCurve(float sellImpact, float buyImpact)
    {
        await RunTest((purchases, market, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketSellImpact, sellImpact);
            configuration.SetCVar(EXCVars.DynamicMarketBuyImpact, buyImpact);
            var purchase = Quote(purchases, StackPrototype, 100, stackUnits: true);
            market.CommitTransaction(purchase.Transaction);
            Assert.That(market.GetFactor(StackKey), Is.EqualTo(Math.Exp((double)0.08f)).Within(0.00000001));

            var sale = market.CalculateSequentialSellValue(StackKey, 150, 100, 1, 1.5, null, true);
            Assert.That(purchase.TotalPrice, Is.GreaterThan(sale));
            Assert.That(market.GetFactor(StackKey), Is.EqualTo(1).Within(0.00000001));

            market.SetFactor(StackKey, 1);
            var saleFirst = market.CalculateSequentialSellValue(StackKey, 150, 100, 1, 1.5, null, true);
            var repurchase = Quote(purchases, StackPrototype, 100, stackUnits: true);
            market.CommitTransaction(repurchase.Transaction);
            Assert.That(repurchase.TotalPrice, Is.GreaterThan(saleFirst));
            Assert.That(market.GetFactor(StackKey), Is.EqualTo(1).Within(0.00000001));
        });
    }

    [TestCase(25)]
    [TestCase(50)]
    public async Task RandomContentsShareTheResaleBoundWithoutCommittingHypotheticalStock(int randomUnits)
    {
        await RunTest((purchases, market, _) =>
        {
            var randomOnly = Quote(purchases, "ExodusPurchaseBoundaryRandomStack", 1);
            Assert.That(randomOnly.Transaction.Factors, Is.Empty,
                "Possible random outcomes must not create demand for inventory that was never delivered.");

            var requests = new MarketPurchaseRequest[]
            {
                new(StackPrototype, 100, 150, StackUnits: true),
                new("ExodusPurchaseBoundaryRandomStack", 1, 150),
            };
            Assert.That(purchases.TryQuotePurchase(requests, 0, out var purchase, ceiling: Ceiling), Is.True);
            Assert.That(market.GetFactor(StackKey), Is.EqualTo(1));
            market.CommitTransaction(purchase!.Transaction);
            Assert.That(market.GetFactor(StackKey), Is.EqualTo(Math.Exp((double)0.08f)).Within(0.00000001),
                "Only the 100 exact stack units may contribute purchase pressure.");

            var actualResale = market.CalculateSequentialSellValue(StackKey, 150, 100 + randomUnits, 1, 1.5, null, true);
            Assert.That(purchase.TotalPrice, Is.GreaterThanOrEqualTo(actualResale * 1.05));

            market.SetFactor(StackKey, 1);
            var saleFirst = market.CalculateSequentialSellValue(StackKey, 150, 100 + randomUnits, 1, 1.5, null, true);
            Assert.That(purchases.TryQuotePurchase(requests, 0, out var replacement, ceiling: Ceiling), Is.True);
            Assert.That(replacement!.TotalPrice, Is.GreaterThanOrEqualTo(saleFirst * 1.05),
                "Replacing either possible random outcome cannot recover the proceeds of the original sale.");
        });
    }

    [Test]
    public async Task UnopenedRandomPackageCannotUseAnUnquotedCommodityFactor()
    {
        await RunTest((purchases, market, _, entities) =>
        {
            const string package = "ExodusPurchaseBoundaryRandomPackage";
            market.SetFactor($"proto:{ItemPrototype}", 0.01);
            market.SetFactor($"proto:{package}", 9);
            var purchase = Quote(purchases, package, 1, basePrice: 1);
            var uid = entities.SpawnEntity(package, MapCoordinates.Nullspace);
            try
            {
                Assert.That(entities.System<MarketBasketSystem>().TryGetEntityBasket(uid, out var basket, out var failure),
                    Is.True, failure);
                var transaction = new MarketTransactionState();
                double resale = 0;
                foreach (var line in basket.Lines)
                {
                    resale += market.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice,
                        line.Quantity, 1, 1.5, transaction, false);
                }

                Assert.That(resale, Is.GreaterThan(100), "The unopened package has its own high market factor.");
                Assert.That(purchase.TotalPrice, Is.GreaterThanOrEqualTo(resale * 1.05),
                    "Cheap possible contents must not hide the separate liquidation route for their unopened wrapper.");
                Assert.That(purchase.Transaction.Factors, Is.Empty);
            }
            finally
            {
                entities.DeleteEntity(uid);
            }
        });
    }

    [Test]
    public async Task StaticPricesStillHaveAResaleFloorAndKeepExpensiveCatalogPrices()
    {
        await RunTest((purchases, market, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            var cheap = Quote(purchases, ItemPrototype, 1);
            var expensive = Quote(purchases, ItemPrototype, 1, basePrice: 1000);
            Assert.Multiple(() =>
            {
                Assert.That(cheap.NominalPrice, Is.EqualTo(150));
                Assert.That(cheap.TotalPrice, Is.EqualTo(237), "Static resale is 150 × 1.5 = 225, so the 5% floor rounds up to 237.");
                Assert.That(expensive.NominalPrice, Is.EqualTo(1000));
                Assert.That(expensive.TotalPrice, Is.EqualTo(1000));
                Assert.That(expensive.Adjustment, Is.Zero);
                Assert.That(cheap.Transaction.Factors, Is.Empty);
                Assert.That(market.GetFactor($"proto:{ItemPrototype}"), Is.EqualTo(1));
            });
        });
    }

    [Test]
    public async Task ReturningAllNominalRevenueCannotRefundTheProtectiveAdjustment()
    {
        await RunTest((purchases, _, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            Assert.That(purchases.TryQuotePrototypeBuy(ItemPrototype, 1, 150, 1, out var purchase,
                purchaseReturnRate: 1, ceiling: Ceiling), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(purchase!.NominalPrice, Is.EqualTo(150));
                Assert.That(purchase.TotalPrice, Is.EqualTo(387));
                Assert.That(purchase.Adjustment, Is.EqualTo(237));
                Assert.That(purchase.TotalPrice - purchase.NominalPrice, Is.GreaterThan(225),
                    "Even a cooperating account receiving all nominal revenue cannot fund the buy/sell cycle.");
            });

            Assert.That(purchases.TryQuotePrototypeBuy(TaxedItemPrototype, 1, 150, 1, out var taxed,
                ceiling: Ceiling), Is.True);
            Assert.That(taxed!.TotalPrice, Is.EqualTo(426), "225 paid to the seller plus 180 in sale taxes require a charge above 405.");
        });
    }

    [Test]
    public async Task NonCashSalesCannotBypassTheFloorThroughCreditTaxPayouts()
    {
        await RunTest((purchases, _, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            var ceiling = new MarketSellCeiling(1.5, 1.5, NonCashPalletMultiplier: 100);
            Assert.That(purchases.TryQuotePrototypeBuy(TaxedItemPrototype, 1, 150, 1, out var purchase, ceiling: ceiling), Is.True);
            const int creditTaxPayout = 12_000; // 150 nominal value * 100 console modifier * 0.8 ItemTax.
            Assert.That(purchase!.TotalPrice, Is.GreaterThanOrEqualTo(creditTaxPayout * 1.05));

            Assert.That(purchases.TryQuotePrototypeBuy(ItemPrototype, 1, 150, 1, out var noTax, ceiling: ceiling), Is.True);
            Assert.That(noTax!.TotalPrice, Is.EqualTo(237), "The separate currency itself is not a credit payout.");
        });
    }

    [TestCase("CrateTradeSecureNormal", 1000, 750, 5750, true)]
    [TestCase("CrateTradeSecureNormal", 1000, 750, 5750, false)]
    [TestCase("CrateTradeSecureHigh", 2000, 1500, 9000, true)]
    [TestCase("CrateTradeSecureHigh", 2000, 1500, 9000, false)]
    public async Task TradeCratesCoverLocalResaleButLeaveDeliveryProfit(
        string prototype, int nominalPrice, int elsewhereReward, int deliveryReward, bool dynamicMarket)
    {
        await RunTest((purchases, market, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, dynamicMarket);
            var ceiling = new MarketSellCeiling(1.5, 1.5);
            Assert.That(purchases.TryQuotePrototypeBuy(prototype, 1, nominalPrice, 1,
                out var purchase, ceiling: ceiling), Is.True);
            market.CommitTransaction(purchase!.Transaction);
            var localPayout = market.CalculateSequentialSellValue($"proto:{prototype}", elsewhereReward * 1.3,
                1, 1, 1, null, false);
            var deliveredPayout = market.CalculateSequentialSellValue($"proto:{prototype}", deliveryReward,
                1, 1, 1, null, false);
            Assert.Multiple(() =>
            {
                Assert.That(purchase.TotalPrice, Is.GreaterThanOrEqualTo(localPayout * 1.05 - 0.000001),
                    "Reselling without delivery, including the Frontier tax payout, must not generate money.");
                Assert.That(purchase.TotalPrice, Is.LessThan(DynamicMarketSystem.RoundSellPayout(deliveredPayout)),
                    "The destination reward must pay for hauling the purchased crate.");
            });
        });
    }

    [Test]
    public async Task TradeCrateFloorCoversOwnValueAndLateElsewhereReward()
    {
        await RunTest((purchases, _, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            var ceiling = new MarketSellCeiling(1, 1);
            Assert.That(purchases.TryQuotePrototypeBuy("ExodusPurchaseBoundaryTradeCrate", 1, 100, 1,
                out var purchase, ceiling: ceiling), Is.True);
            // Static value 10 plus the late elsewhere reward 50 - (-100) = 150 must be covered.
            Assert.That(purchase!.TotalPrice, Is.GreaterThanOrEqualTo(168));
            Assert.That(purchase.TotalPrice, Is.LessThan(240),
                "The timely destination reward (200 + 30 + 10) remains income for delivery.");
        });
    }

    [Test]
    public async Task DeliveryBonusesDoNotIncreaseTradeCratePurchasePrice()
    {
        await RunTest((purchases, _, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            var ceiling = new MarketSellCeiling(1, 1);
            Assert.That(purchases.TryQuotePrototypeBuy("ExodusPurchaseBoundaryTradeCrate", 1, 100, 1,
                out var ordinary, ceiling: ceiling), Is.True);
            Assert.That(purchases.TryQuotePrototypeBuy("ExodusPurchaseBoundaryHighRewardTradeCrate", 1, 100, 1,
                out var highReward, ceiling: ceiling), Is.True);
            Assert.That(highReward!.TotalPrice, Is.EqualTo(ordinary!.TotalPrice),
                "Increasing the reward for completing a route must benefit the hauler.");
        });
    }

    [Test]
    public async Task NonExpressTradeCratesDoNotPriceAnInactiveLatePenalty()
    {
        await RunTest((purchases, _, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            Assert.That(purchases.TryQuotePrototypeBuy("ExodusPurchaseBoundaryNonExpressTradeCrate", 1, 100, 1,
                out var purchase, ceiling: new MarketSellCeiling(1, 1)), Is.True);
            Assert.That(purchase!.TotalPrice, Is.EqualTo(100),
                "The catalog price already covers the 60-credit resale; no express timer means no late reward.");
        });
    }

    [Test]
    public async Task TradeCrateContentsRemainProtectedFromImmediateResale()
    {
        await RunTest((purchases, _, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            Assert.That(purchases.TryQuotePrototypeBuy("ExodusPurchaseBoundaryLoadedTradeCrate", 1, 1000, 1,
                out var purchase, ceiling: new MarketSellCeiling(1.5, 1.5)), Is.True);
            // Elsewhere value 750 with its 30% tax, plus an untaxed 150-credit item: 1125 * 1.5 * 1.05.
            Assert.That(purchase!.TotalPrice, Is.GreaterThanOrEqualTo(1771.875),
                "Excluding delivery rewards must not exempt a trade crate's ordinary contents from the floor.");
        });
    }

    [Test]
    public async Task InvalidAndUnrepresentableQuotesFailWithoutMovingTheMarket()
    {
        await RunTest((purchases, market, _) =>
        {
            var requests = new (int Quantity, double Price, double Modifier)[]
            {
                (0, 150, 1),
                (-1, 150, 1),
                (1, double.NaN, 1),
                (1, double.PositiveInfinity, 1),
                (1, -1, 1),
                (1, 150, double.NaN),
                (1, 150, double.PositiveInfinity),
                (1, 150, -1),
                (1, 150, 0),
                (int.MaxValue, 150, 1),
                (1, int.MaxValue, 2),
            };
            foreach (var (quantity, price, modifier) in requests)
            {
                Assert.That(purchases.TryQuotePrototypeBuy(ItemPrototype, quantity, price, modifier,
                    out var quote, ceiling: Ceiling), Is.False, $"quantity={quantity}, price={price}, modifier={modifier}");
                Assert.That(quote, Is.Null);
            }

            var valid = new MarketPurchaseRequest(ItemPrototype, 1, 150);
            foreach (var invalid in new[] { -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.That(purchases.TryQuotePurchase([valid], invalid, out var feeQuote, ceiling: Ceiling), Is.False);
                Assert.That(feeQuote, Is.Null);
                Assert.That(purchases.TryQuotePurchase([valid], 0, out var returnQuote,
                    purchaseReturnRate: invalid, ceiling: Ceiling), Is.False);
                Assert.That(returnQuote, Is.Null);
            }

            Assert.That(purchases.TryQuotePurchase([], 0, out var emptyQuote, ceiling: Ceiling), Is.False);
            Assert.That(emptyQuote, Is.Null);
            Assert.That(market.GetFactor($"proto:{ItemPrototype}"), Is.EqualTo(1));
            Assert.That(market.GetFactor(StackKey), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task LargestRepresentableChargeRemainsPurchasable()
    {
        await RunTest((purchases, _, configuration) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            Assert.That(purchases.TryQuotePrototypeBuy(ItemPrototype, 1, int.MaxValue - 0.5, 1,
                out var largest, ceiling: Ceiling), Is.True);
            Assert.That(largest!.TotalPrice, Is.EqualTo(int.MaxValue));
            Assert.That(purchases.TryQuotePrototypeBuy(ItemPrototype, 1, int.MaxValue + 0.25, 1,
                out var overflow, ceiling: Ceiling), Is.False);
            Assert.That(overflow, Is.Null);
        });
    }

    private static MarketPurchaseQuote Quote(
        MarketPurchaseSystem purchases,
        string prototype,
        int quantity,
        double basePrice = 150,
        bool stackUnits = false)
    {
        Assert.That(purchases.TryQuotePrototypeBuy(prototype, quantity, basePrice, 1, out var quote,
            stackUnits: stackUnits, ceiling: Ceiling), Is.True);
        return quote!;
    }

    private static Task RunTest(Action<MarketPurchaseSystem, DynamicMarketSystem, IConfigurationManager> assertion)
    {
        return RunTest((purchases, market, configuration, _) => assertion(purchases, market, configuration));
    }

    private static async Task RunTest(Action<MarketPurchaseSystem, DynamicMarketSystem, IConfigurationManager, IEntityManager> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var configuration = server.ResolveDependency<IConfigurationManager>();
            var market = server.System<DynamicMarketSystem>();
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
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            for (var i = 0; i < definitions.Length; i++)
                configuration.SetCVar(definitions[i], definitions[i].DefaultValue);
            market.ResetAll();

            try
            {
                assertion(server.System<MarketPurchaseSystem>(), market, configuration, server.ResolveDependency<IEntityManager>());
            }
            finally
            {
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
