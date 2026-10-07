// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.Linq;
using Content.Server._Exodus.Economy;
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketGasImpactTest
{
    private const string OxygenKey = "gas:Oxygen";
    private const string NitrogenKey = "gas:Nitrogen";
    private const string ItemKey = "proto:ExodusGasImpactItem";
    private const string TankKey = "proto:ExodusGasImpactTank";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusGasImpactItem
          components:
          - type: Item
          - type: StaticPrice
            price: 100

        - type: entity
          id: ExodusGasImpactTank
          parent: ExodusGasImpactItem
          components:
          - type: GasTank
            air:
              volume: 10000
              temperature: 293.15
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task GasUsesTheConfiguredFractionOfOrdinaryCommodityPressurePerUnit(bool sell)
    {
        await RunTest((market, _) =>
        {
            if (sell)
            {
                market.CalculateSequentialSellValue(OxygenKey, 1, 1000, 100, 1, null, true);
                market.CalculateSequentialSellValue(ItemKey, 1, 1000, 100, 1, null, true);
            }
            else
            {
                market.CalculateSequentialBuyCost(OxygenKey, 1, 1000, 100, 1, null, true);
                market.CalculateSequentialBuyCost(ItemKey, 1, 1000, 100, 1, null, true);
            }

            var direction = sell ? -1 : 1;
            Assert.Multiple(() =>
            {
                Assert.That(market.GetFactor(OxygenKey), Is.EqualTo(Math.Exp(direction * 0.0024)).Within(0.0000001));
                Assert.That(market.GetFactor(ItemKey), Is.EqualTo(Math.Exp(direction * 0.8)).Within(0.0000001));
                Assert.That(Math.Log(market.GetFactor(OxygenKey)) / Math.Log(market.GetFactor(ItemKey)),
                    Is.EqualTo(0.003).Within(0.00000001));
            });
        });
    }

    [Test]
    public async Task SellingAFilledTankKeepsTheOriginalShellImpact()
    {
        await RunTest((market, entities) =>
        {
            var tank = entities.SpawnEntity("ExodusGasImpactTank", MapCoordinates.Nullspace);
            try
            {
                var air = entities.GetComponent<GasTankComponent>(tank).Air;
                air.SetMoles(Gas.Oxygen, 1000);
                var unitPrice = entities.System<AtmosphereSystem>().GetGas((int) Gas.Oxygen).PricePerMole;
                var payout = market.CalculateGasContainerSellValue(tank, air, 1, null, true, usePurity: false);
                var expected = 100 * (1 - Math.Exp(-0.0008)) / 0.0008
                    + unitPrice * (1 - Math.Exp(-0.0024)) / 0.0000024;
                Assert.Multiple(() =>
                {
                    Assert.That(payout, Is.EqualTo(expected).Within(0.0001));
                    Assert.That(market.GetFactor(TankKey), Is.EqualTo(Math.Exp(-0.0008)).Within(0.0000001));
                    Assert.That(market.GetFactor(OxygenKey), Is.EqualTo(Math.Exp(-0.0024)).Within(0.0000001));
                });
            }
            finally
            {
                entities.DeleteEntity(tank);
            }
        });
    }

    [Test]
    public async Task MixedGasPreviewMatchesTheSaleAndCommitsEachSpeciesOwnMoles()
    {
        await RunTest((market, entities) =>
        {
            var air = new GasMixture(10000);
            air.SetMoles(Gas.Oxygen, 1000);
            air.SetMoles(Gas.Nitrogen, 500);
            var atmos = entities.System<AtmosphereSystem>();
            var oxygenPrice = atmos.GetGas((int) Gas.Oxygen).PricePerMole;
            var nitrogenPrice = atmos.GetGas((int) Gas.Nitrogen).PricePerMole;
            const double consoleModifier = 0.7;
            var expected = consoleModifier * (oxygenPrice * (1 - Math.Exp(-0.0024))
                + nitrogenPrice * (1 - Math.Exp(-0.0012))) / 0.0000024;
            var transaction = new MarketTransactionState();

            var lines = market.BuildGasMarketLines(air, consoleModifier, usePurity: false);
            var preview = market.CalculateGasMixtureSellValue(air, consoleModifier, transaction, false, false);
            Assert.Multiple(() =>
            {
                Assert.That(lines.Count, Is.EqualTo(2));
                Assert.That(preview, Is.EqualTo(expected).Within(0.0001));
                Assert.That(lines.Sum(line => line.LineTotal), Is.EqualTo(DynamicMarketSystem.RoundSellPayout(preview)));
                Assert.That(lines.Sum(line => line.UnitPrice * line.Moles), Is.EqualTo(preview).Within(0.000001));
                Assert.That(market.GetFactor(OxygenKey), Is.EqualTo(1), "Appraisal must not move the live market.");
                Assert.That(market.GetFactor(NitrogenKey), Is.EqualTo(1));
            });

            market.CommitTransaction(transaction);
            Assert.Multiple(() =>
            {
                Assert.That(market.GetFactor(OxygenKey), Is.EqualTo(Math.Exp(-0.0024)).Within(0.0000001));
                Assert.That(market.GetFactor(NitrogenKey), Is.EqualTo(Math.Exp(-0.0012)).Within(0.0000001));
            });
        });
    }

    [Test]
    public async Task SplittingAGasSaleKeepsItsTotalValueAndFinalMarketPressure()
    {
        await RunTest((market, entities) =>
        {
            var air = new GasMixture(10000);
            air.SetMoles(Gas.Oxygen, 1000);
            var bulkValue = market.CalculateGasMixtureSellValue(air, 1, null, true, false);
            var bulkFactor = market.GetFactor(OxygenKey);
            market.SetFactor(OxygenKey, 1);
            air.SetMoles(Gas.Oxygen, 100);
            double splitValue = 0;
            var splitPayout = 0;
            for (var i = 0; i < 10; i++)
            {
                var value = market.CalculateGasMixtureSellValue(air, 1, null, true, false);
                splitValue += value;
                splitPayout += DynamicMarketSystem.RoundSellPayout(value);
            }

            Assert.Multiple(() =>
            {
                Assert.That(splitValue, Is.EqualTo(bulkValue).Within(0.000001));
                Assert.That(market.GetFactor(OxygenKey), Is.EqualTo(bulkFactor).Within(0.0000001));
                Assert.That(bulkFactor, Is.EqualTo(Math.Exp(-0.0024)).Within(0.0000001));
                Assert.That(splitPayout, Is.LessThanOrEqualTo(DynamicMarketSystem.RoundSellPayout(bulkValue)));
            });
        });
    }

    [Test]
    public async Task BuyingThenSellingGasRetracesTheSameCurveWithoutProfit()
    {
        await RunTest((market, _) =>
        {
            var buy = market.CalculateSequentialBuyCost(OxygenKey, 1, 1000, 100, 1, null, true);
            var sell = market.CalculateSequentialSellValue(OxygenKey, 1, 1000, 100, 1, null, true);
            Assert.Multiple(() =>
            {
                Assert.That(buy, Is.EqualTo((Math.Exp(0.0024) - 1) / 0.0000024).Within(0.0001));
                Assert.That(sell, Is.EqualTo(buy).Within(0.000001));
                Assert.That(market.GetFactor(OxygenKey), Is.EqualTo(1).Within(0.0000001));
                Assert.That(DynamicMarketSystem.RoundSellPayout(sell), Is.LessThanOrEqualTo(DynamicMarketSystem.RoundBuyCost(buy)));
            });
        });
    }

    private static async Task RunTest(Action<DynamicMarketSystem, IEntityManager> assertion)
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
            };
            var previous = new float[definitions.Length];
            for (var i = 0; i < definitions.Length; i++)
                previous[i] = configuration.GetCVar(definitions[i]);
            var quotes = new Dictionary<string, MarketQuote?>();
            foreach (var key in new[] { OxygenKey, NitrogenKey, ItemKey, TankKey })
                quotes[key] = market.GetAllQuotes().TryGetValue(key, out var quote) ? quote : null;

            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            for (var i = 0; i < definitions.Length; i++)
                configuration.SetCVar(definitions[i], definitions[i].DefaultValue);
            configuration.SetCVar(EXCVars.DynamicMarketDecayRate, 0f);
            foreach (var key in quotes.Keys)
                market.SetFactor(key, 1);
            try
            {
                assertion(market, server.EntMan);
            }
            finally
            {
                for (var i = 0; i < definitions.Length; i++)
                    configuration.SetCVar(definitions[i], previous[i]);
                foreach (var (key, previousQuote) in quotes)
                {
                    if (previousQuote is { } quote)
                    {
                        market.SetFactor(key, quote.PreviousFactor);
                        market.SetFactor(key, quote.Factor);
                    }
                    else
                    {
                        market.ResetKey(key);
                    }
                }
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
