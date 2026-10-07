// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.Diagnostics;
using Content.Server._Exodus.Economy;
using Content.Server._NF.Market.Extensions;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared._NF.Market;
using Content.Shared.Cargo.Components;
using Content.Shared.Stacks;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class DynamicMarketTest
{
    [TestPrototypes]
    private const string PricePrototypes = @"
- type: entity
  id: ExodusEconomyExplicitZeroPrice
  components:
  - type: Item
  - type: StaticPrice
    price: 0

- type: entity
  id: ExodusEconomyUnpricedItem
  components:
  - type: Item

- type: entity
  id: ExodusEconomyUnpricedStack
  components:
  - type: Item
  - type: Stack
    stackType: ExodusEconomyTestStack
    count: 10

- type: stack
  id: ExodusEconomyTestStack
  name: stack-steel
  spawn: ExodusEconomyUnpricedStack
  maxCount: 50

- type: entity
  id: ExodusEconomyBatchProduct
  components:
  - type: Item

- type: material
  id: ExodusEconomyTestMaterial
  icon:
    sprite: Objects/Materials/Sheets/metal.rsi
    state: steel
  price: 10

- type: latheRecipe
  id: ExodusEconomyTestBatchRecipe
  result: ExodusEconomyBatchProduct
  resultCount: 4
  materials:
    ExodusEconomyTestMaterial: 100

- type: entity
  id: ExodusEconomyGasContainer
  components:
  - type: Item
  - type: StaticPrice
    price: 100
  - type: GasTank
    air:
      volume: 10
      temperature: 293.15
      moles:
      - 10

- type: entity
  id: ExodusEconomyCheapBatchProduct
  components:
  - type: Item

- type: entity
  parent: ExodusEconomyCheapBatchProduct
  id: ExodusEconomyCheapMachinePart
  components:
  - type: MachinePart
    part: Capacitor
    rating: 4

- type: latheRecipe
  id: ExodusEconomyCheapBatchRecipe
  result: ExodusEconomyCheapBatchProduct
  resultCount: 4
  materials:
    ExodusEconomyTestMaterial: 1

- type: latheRecipe
  id: ExodusEconomyCheapMachinePartRecipe
  result: ExodusEconomyCheapMachinePart
  materials:
    ExodusEconomyTestMaterial: 1
";

    [Test]
    public void MarketStockUsesWeightedBasePrice()
    {
        var stock = new List<MarketData>
        {
            new("ExodusEconomyUnpricedItem", null, 2, 100.0),
        };

        stock.Upsert("ExodusEconomyUnpricedItem", 2, 200.0);

        Assert.Multiple(() =>
        {
            Assert.That(stock[0].Quantity, Is.EqualTo(4));
            Assert.That(stock[0].Price, Is.EqualTo(150.0));
        });
    }

    [Test]
    public void OversizedPricesSaturateInsteadOfOverflowing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DynamicMarketSystem.RoundToPrice(double.PositiveInfinity), Is.EqualTo(int.MaxValue));
            Assert.That(DynamicMarketSystem.RoundToPrice(double.NaN), Is.Zero);
            Assert.That(DynamicMarketSystem.RoundToInt(double.NegativeInfinity), Is.EqualTo(int.MinValue));
            Assert.That(DynamicMarketSystem.RoundBuyCost(double.PositiveInfinity), Is.EqualTo(int.MaxValue));
            Assert.That(DynamicMarketSystem.RoundSellPayout(double.PositiveInfinity), Is.EqualTo(int.MaxValue));
            Assert.That(DynamicMarketSystem.RoundBuyCost(double.NaN), Is.Zero);
            Assert.That(DynamicMarketSystem.RoundSellPayout(double.NaN), Is.Zero);
        });
    }

    [TestCase(0.75, 4)]
    [TestCase(2.51, 20)]
    [TestCase(10.9, 100)]
    public void FractionalRoundingCannotProfitFromSplitTransactions(double price, int quantity)
    {
        Assert.Multiple(() =>
        {
            Assert.That(DynamicMarketSystem.RoundSellPayout(price) * quantity,
                Is.LessThanOrEqualTo(DynamicMarketSystem.RoundBuyCost(price * quantity)));
            Assert.That(DynamicMarketSystem.RoundBuyCost(price) * quantity,
                Is.GreaterThanOrEqualTo(DynamicMarketSystem.RoundBuyCost(price * quantity)));
            Assert.That(DynamicMarketSystem.RoundSellPayout(price) * quantity,
                Is.LessThanOrEqualTo(DynamicMarketSystem.RoundSellPayout(price * quantity)));
        });
    }

    [Test]
    public async Task BuyThenSellSameLotsHasNoArbitrage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();

        await server.WaitAssertion(() =>
        {
            const string key = "test:round-trip";
            market.SetFactor(key, 1.0);

            var buyTransaction = new MarketTransactionState();
            var buyCost = market.CalculateSequentialBuyCost(
                key,
                unitBasePrice: 10.0,
                totalUnits: 100,
                lotSize: 10,
                consoleMod: 1.0,
                tx: buyTransaction,
                applyImpact: false);
            market.CommitTransaction(buyTransaction);

            var sellTransaction = new MarketTransactionState();
            var sellValue = market.CalculateSequentialSellValue(
                key,
                unitBasePrice: 10.0,
                totalUnits: 100,
                lotSize: 10,
                consoleMod: 1.0,
                tx: sellTransaction,
                applyImpact: false);
            market.CommitTransaction(sellTransaction);

            Assert.Multiple(() =>
            {
                Assert.That(sellValue, Is.EqualTo(buyCost).Within(0.000001));
                Assert.That(market.GetFactor(key), Is.EqualTo(1.0).Within(0.000001));
            });

            market.ResetKey(key);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FractionalGasUsesActualMoles()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();

        await server.WaitAssertion(() =>
        {
            var quarterMole = new GasMixture(10f);
            quarterMole.AdjustMoles(Gas.Oxygen, 0.25f);

            var halfMole = new GasMixture(10f);
            halfMole.AdjustMoles(Gas.Oxygen, 0.5f);

            var quarterValue = market.CalculateGasMixtureSellValue(
                quarterMole,
                consoleMod: 1.0,
                tx: null,
                applyImpact: false,
                usePurity: false);
            var halfValue = market.CalculateGasMixtureSellValue(
                halfMole,
                consoleMod: 1.0,
                tx: null,
                applyImpact: false,
                usePurity: false);

            Assert.That(halfValue, Is.GreaterThan(quarterValue * 1.9));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExplicitZeroPriceReceivesIntentionalFallback()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var pricing = server.System<PricingSystem>();

        await server.WaitAssertion(() =>
        {
            var explicitlyZeroPriced = entities.SpawnEntity("ExodusEconomyExplicitZeroPrice", MapCoordinates.Nullspace);
            var unpriced = entities.SpawnEntity("ExodusEconomyUnpricedItem", MapCoordinates.Nullspace);

            Assert.Multiple(() =>
            {
                Assert.That(pricing.GetPrice(explicitlyZeroPriced), Is.InRange(1.0, 25.0));
                Assert.That(pricing.GetPrice(unpriced), Is.InRange(1.0, 25.0));
            });

            entities.DeleteEntity(explicitlyZeroPriced);
            entities.DeleteEntity(unpriced);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CashPrototypesRemainCargoBlacklisted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var cash = entities.SpawnEntity("SpaceCash", MapCoordinates.Nullspace);
            var counterfeit = entities.SpawnEntity("SpaceCashCounterfeit", MapCoordinates.Nullspace);

            Assert.Multiple(() =>
            {
                Assert.That(entities.HasComponent<CargoSellBlacklistComponent>(cash), Is.True);
                Assert.That(entities.HasComponent<CargoSellBlacklistComponent>(counterfeit), Is.True);
            });

            entities.DeleteEntity(cash);
            entities.DeleteEntity(counterfeit);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InvalidFactorIsSanitized()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();

        await server.WaitAssertion(() =>
        {
            const string key = "test:invalid-factor";
            market.SetFactor(key, double.NaN);

            Assert.That(market.GetFactor(key), Is.EqualTo(1.0));
            market.ResetKey(key);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ZeroModifierDoesNotMoveMarket()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();

        await server.WaitAssertion(() =>
        {
            const string key = "test:zero-modifier";
            market.SetFactor(key, 1.0);

            var transaction = new MarketTransactionState();
            var value = market.CalculateSequentialSellValue(
                key,
                unitBasePrice: 10.0,
                totalUnits: 100,
                lotSize: 10,
                consoleMod: 0,
                tx: transaction,
                applyImpact: false);
            market.CommitTransaction(transaction);

            Assert.Multiple(() =>
            {
                Assert.That(value, Is.Zero);
                Assert.That(market.GetFactor(key), Is.EqualTo(1.0));
            });

            market.ResetKey(key);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnpricedStacksKeepValueWhenSplit()
    {
        await RunMarketTest((_, entities, _) =>
        {
            var pricing = entities.System<PricingSystem>();
            var stacks = entities.System<SharedStackSystem>();
            var uid = entities.SpawnEntity("ExodusEconomyUnpricedStack", MapCoordinates.Nullspace);
            var fullPrice = pricing.GetPrice(uid);
            Assert.That(pricing.GetEstimatedPrice(entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!), Is.EqualTo(fullPrice));
            stacks.SetCount(uid, 1);

            Assert.That(pricing.GetPrice(uid) * 10, Is.EqualTo(fullPrice).Within(0.000001));
            entities.DeleteEntity(uid);
        });
    }

    [Test]
    public async Task RecipeFallbackDividesCostAcrossBatchOutputs()
    {
        await RunMarketTest((_, entities, _) =>
        {
            var pricing = entities.System<PricingSystem>();
            var uid = entities.SpawnEntity("ExodusEconomyBatchProduct", MapCoordinates.Nullspace);
            Assert.That(pricing.GetPrice(uid), Is.EqualTo(1000.0 / 4 * 0.65));
            entities.DeleteEntity(uid);
        });
    }

    [TestCase("ExodusEconomyCheapBatchProduct", 4)]
    [TestCase("ExodusEconomyCheapMachinePart", 1)]
    public async Task CheapCraftedItemsCannotExceedTheirMaterialCost(string prototype, int outputCount)
    {
        await RunMarketTest((_, entities, _) =>
        {
            var pricing = entities.System<PricingSystem>();
            var uid = entities.SpawnEntity(prototype, MapCoordinates.Nullspace);
            var total = pricing.GetPrice(uid) * outputCount;
            Assert.That(total, Is.EqualTo(10 * 0.65).Within(0.000001));
            entities.DeleteEntity(uid);
        });
    }

    [Test]
    public async Task ContainerContentsDoNotReplaceFallbackValue()
    {
        await RunMarketTest((_, entities, _) =>
        {
            var pricing = entities.System<PricingSystem>();
            var containers = entities.System<SharedContainerSystem>();
            var bag = entities.SpawnEntity("ExodusEconomyUnpricedItem", MapCoordinates.Nullspace);
            var item = entities.SpawnEntity("ExodusEconomyExplicitZeroPrice", MapCoordinates.Nullspace);
            var emptyPrice = pricing.GetPrice(bag);
            var contentsPrice = pricing.GetPrice(item);
            var container = containers.EnsureContainer<Container>(bag, "economy-test");
            Assert.That(containers.Insert(item, container), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(pricing.GetPrice(bag), Is.EqualTo(emptyPrice + contentsPrice));
                Assert.That(pricing.GetPriceWithVendingDiscount(bag, bag), Is.EqualTo(emptyPrice + contentsPrice));
            });
            entities.DeleteEntity(bag);
        });
    }

    [Test]
    public async Task GasContainerPreviewMatchesCommittedSale()
    {
        await RunMarketTest((market, entities, _) =>
        {
            var shell = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            entities.AddComponent<StaticPriceComponent>(shell).Price = 500;
            var air = new GasMixture(1000);
            air.AdjustMoles(Gas.Oxygen, 500);

            var preview = market.CalculateGasContainerSellValue(shell, air, 1, null, false, false);
            var sale = market.CalculateGasContainerSellValue(shell, air, 1, null, true, false);

            Assert.That(sale, Is.EqualTo(preview).Within(0.000001));
            entities.DeleteEntity(shell);
        });
    }

    [Test]
    public async Task HugeBuyLotReachesCeiling()
    {
        await RunMarketTest((market, _, cfg) =>
        {
            const string key = "test:huge-buy";
            var transaction = new MarketTransactionState();
            var cost = market.CalculateSequentialBuyCost(key, 1, 1_000_000, 1_000_000, 1, transaction, false);
            market.CommitTransaction(transaction);

            Assert.Multiple(() =>
            {
                Assert.That(market.GetFactor(key), Is.EqualTo(cfg.GetCVar(EXCVars.DynamicMarketMaxFactor)));
                Assert.That(cost, Is.GreaterThan(1_000_000));
            });
        });
    }

    [Test]
    public async Task TraceGasDoesNotMultiplyShellValue()
    {
        await RunMarketTest((market, entities, cfg) =>
        {
            cfg.SetCVar(EXCVars.DynamicMarketSellImpact, 0f);
            cfg.SetCVar(EXCVars.DynamicMarketBuyImpact, 0f);
            market.SetFactor(DynamicMarketSystem.GasKey(Gas.Oxygen), 9);
            var shell = entities.SpawnEntity("ExodusEconomyExplicitZeroPrice", MapCoordinates.Nullspace);
            entities.GetComponent<StaticPriceComponent>(shell).Price = 100;
            var air = new GasMixture(10);
            air.AdjustMoles(Gas.Oxygen, 0.005f);
            var gasValue = market.CalculateGasMixtureSellValue(air, 1, null, false, false);
            var total = market.CalculateGasContainerSellValue(shell, air, 1, null, false, false);

            Assert.Multiple(() =>
            {
                Assert.That(gasValue, Is.GreaterThan(0));
                Assert.That(total, Is.EqualTo(100 + gasValue).Within(0.000001));
            });
            entities.DeleteEntity(shell);
        });
    }

    [Test]
    public async Task GasContainerKeepsDiscountedShellAppraisal()
    {
        await RunMarketTest((market, entities, cfg) =>
        {
            cfg.SetCVar(EXCVars.DynamicMarketSellImpact, 0f);
            cfg.SetCVar(EXCVars.DynamicMarketBuyImpact, 0f);
            var uid = entities.SpawnEntity("ExodusEconomyGasContainer", MapCoordinates.Nullspace);
            entities.GetComponent<GasTankComponent>(uid).Air = new GasMixture(10);

            Assert.That(market.CalculateEntitySellValue(uid, 50, 1, null, false), Is.EqualTo(50));
            entities.DeleteEntity(uid);
        });
    }

    [Test]
    public async Task CatalogPricesShellAndGasIndependently()
    {
        await RunMarketTest((market, _, cfg) =>
        {
            cfg.SetCVar(EXCVars.DynamicMarketBuyImpact, 0f);
            cfg.SetCVar(EXCVars.DynamicMarketSellImpact, 0f);
            market.SetFactor(DynamicMarketSystem.GasKey(Gas.Oxygen), 0.2);
            market.SetFactor(DynamicMarketSystem.ProtoKey("ExodusEconomyGasContainer"), 1.5);
            var cost = market.CalculatePrototypeBuyCost("ExodusEconomyGasContainer", 200, 3, 1, null, false);

            Assert.That(cost, Is.EqualTo(3 * (100 * 1.5 + 100 * 0.2)).Within(0.000001));
        });
    }

    [Test]
    public async Task CatalogStackPressureCountsAllSpawnedUnits()
    {
        await RunMarketTest((market, _, cfg) =>
        {
            const string key = "stack:ExodusEconomyTestStack";
            market.CalculatePrototypeBuyCost("ExodusEconomyUnpricedStack", 100, 2, 1, null, true);
            var expected = Math.Exp(cfg.GetCVar(EXCVars.DynamicMarketBuyImpact) * 20.0 /
                cfg.GetCVar(EXCVars.DynamicMarketReferenceVolume));

            Assert.That(market.GetFactor(key), Is.EqualTo(expected).Within(0.000001));
        });
    }

    [Test]
    public async Task GasRowsShowFinalUnitPricesAndTotal()
    {
        await RunMarketTest((market, _, cfg) =>
        {
            cfg.SetCVar(EXCVars.DynamicMarketSellImpact, 0f);
            cfg.SetCVar(EXCVars.DynamicMarketBuyImpact, 0f);
            var air = new GasMixture(100);
            air.AdjustMoles(Gas.Oxygen, 12.75f);
            air.AdjustMoles(Gas.Plasma, 8.25f);
            var lines = market.BuildGasMarketLines(air, 0.7, false);
            var total = market.CalculateGasMixtureSellValue(air, 0.7, null, false, false);
            var displayTotal = 0;
            double unitTotal = 0;
            foreach (var line in lines)
            {
                displayTotal += line.LineTotal;
                unitTotal += line.UnitPrice * line.Moles;
            }

            Assert.Multiple(() =>
            {
                Assert.That(displayTotal, Is.EqualTo(DynamicMarketSystem.RoundSellPayout(total)));
                Assert.That(unitTotal, Is.EqualTo(total).Within(0.000001));
            });
        });
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task IntegratedPriceMatchesKnownExponentialArea(bool isSell, bool reachesLimit)
    {
        await RunMarketTest((market, _, cfg) =>
        {
            const string key = "test:integral";
            cfg.SetCVar(EXCVars.DynamicMarketMinFactor, 1f);
            cfg.SetCVar(EXCVars.DynamicMarketMaxFactor, 2f);
            cfg.SetCVar(EXCVars.DynamicMarketSellImpact, 1f);
            cfg.SetCVar(EXCVars.DynamicMarketBuyImpact, 1f);
            cfg.SetCVar(EXCVars.DynamicMarketReferenceVolume, 1f);
            market.SetFactor(key, isSell ? 2 : 1);

            // Integral of exp(u), or 2*exp(-u), over [0, log(2)] is exactly one.
            var units = Math.Log(2) + (reachesLimit ? 3 : 0);
            var expectedFactor = isSell ? 1.0 : 2.0;
            var expected = 10 * (1 + (reachesLimit ? 3 * expectedFactor : 0));

            var actual = isSell
                ? market.CalculateSequentialSellValue(key, 10, units, 100, 1, null, true)
                : market.CalculateSequentialBuyCost(key, 10, units, 100, 1, null, true);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.EqualTo(expected).Within(0.000001));
                Assert.That(market.GetFactor(key), Is.EqualTo(expectedFactor).Within(0.000001));
            });
        });
    }

    [TestCase(10.0)]
    [TestCase(0.25)]
    public async Task SplittingPurchasedGoodsCannotGenerateProfit(double sellBatch)
    {
        await RunMarketTest((market, _, _) =>
        {
            const string key = "test:split-round-trip";
            var buy = market.CalculateSequentialBuyCost(key, 10, 100, 100, 1, null, true);
            double sell = 0;
            var payout = 0;
            for (var remaining = 100.0; remaining > 0;)
            {
                var units = Math.Min(sellBatch, remaining);
                var value = market.CalculateSequentialSellValue(key, 10, units, sellBatch, 1, null, true);
                sell += value;
                payout += DynamicMarketSystem.RoundSellPayout(value);
                remaining -= units;
            }

            Assert.Multiple(() =>
            {
                Assert.That(sell, Is.EqualTo(buy).Within(0.000001));
                Assert.That(payout, Is.LessThanOrEqualTo(DynamicMarketSystem.RoundBuyCost(buy)));
                Assert.That(market.GetFactor(key), Is.EqualTo(1.0).Within(0.000001));
            });
        });
    }

    [Test]
    public async Task LotSizeDoesNotChangeTransactionPrice()
    {
        await RunMarketTest((market, _, _) =>
        {
            const string key = "test:lot-size";
            var bulk = market.CalculateSequentialBuyCost(key, 10, 100, 100, 1, null, false);
            var singles = market.CalculateSequentialBuyCost(key, 10, 100, 1, 1, null, false);
            Assert.That(singles, Is.EqualTo(bulk).Within(0.000001));
        });
    }

    [Test]
    [CancelAfter(120000)]
    public async Task HugeTransactionWithTinyImpactHasBoundedRuntime()
    {
        await RunMarketTest((market, _, cfg) =>
        {
            cfg.SetCVar(EXCVars.DynamicMarketBuyImpact, 0.00000001f);
            cfg.SetCVar(EXCVars.DynamicMarketSellImpact, 0.00000001f);
            var timer = Stopwatch.StartNew();
            const string key = "test:many-lots";
            var cost = market.CalculateSequentialBuyCost(key, 1, 2_000_000_000, 1, 1, null, true);
            timer.Stop();

            Assert.Multiple(() =>
            {
                Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
                Assert.That(cost, Is.GreaterThan(2_000_000_000));
                Assert.That(market.GetFactor(key), Is.EqualTo(Math.Exp(0.2)).Within(0.000001));
            });
        });
    }

    private static async Task RunMarketTest(Action<DynamicMarketSystem, IEntityManager, IConfigurationManager> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var market = server.System<DynamicMarketSystem>();
            var enabled = cfg.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = cfg.GetCVar(EXCVars.DynamicMarketPersist);
            var definitions = new[]
            {
                EXCVars.DynamicMarketMinFactor,
                EXCVars.DynamicMarketMaxFactor,
                EXCVars.DynamicMarketSellImpact,
                EXCVars.DynamicMarketBuyImpact,
                EXCVars.DynamicMarketReferenceVolume,
                EXCVars.DynamicMarketDecayRate,
            };
            var previous = new float[definitions.Length];
            for (var i = 0; i < definitions.Length; i++)
                previous[i] = cfg.GetCVar(definitions[i]);

            cfg.SetCVar(EXCVars.DynamicMarketPersist, false);
            cfg.SetCVar(EXCVars.DynamicMarketEnabled, true);
            for (var i = 0; i < definitions.Length; i++)
                cfg.SetCVar(definitions[i], definitions[i].DefaultValue);
            market.ResetAll();

            try
            {
                assertion(market, server.ResolveDependency<IEntityManager>(), cfg);
            }
            finally
            {
                market.ResetAll();
                for (var i = 0; i < definitions.Length; i++)
                    cfg.SetCVar(definitions[i], previous[i]);
                cfg.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                cfg.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
