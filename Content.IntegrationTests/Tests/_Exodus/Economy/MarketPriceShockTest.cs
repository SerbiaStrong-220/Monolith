// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.Linq;
using Content.Server._Exodus.Economy;
using Content.Server._Exodus.StationEvents.Components;
using Content.Server.GameTicking;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy;
using Content.Shared.Atmos;
using Content.Shared.GameTicking.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketPriceShockTest
{
    private const string RuleId = "ExodusMarketPriceShock";
    private const string UnrelatedKey = "test:market-price-shock-unrelated";

    [TestCase(10, 0.5)]
    [TestCase(3, 0.25)]
    public async Task StartingTheRuleChangesExactlyTheConfiguredNumberOfDistinctCommodities(int count, double change)
    {
        await RunTest((entities, ticker, market, configuration, rule) =>
        {
            var component = entities.GetComponent<MarketPriceShockRuleComponent>(rule);
#pragma warning disable RA0002 // Configure this fixture before starting the real game rule.
            component.ProductCount = count;
            component.PriceChange = change;
#pragma warning restore RA0002
            market.SetFactor(UnrelatedKey, 1.25);
            var before = market.GetAllQuotes().ToDictionary(entry => entry.Key, entry => entry.Value);

            Assert.That(ticker.StartGameRule(rule), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(component.Changes.Count, Is.EqualTo(count));
                Assert.That(component.Processed, Is.True);
                Assert.That(entities.HasComponent<EndedGameRuleComponent>(rule), Is.True,
                    "An instantaneous price shock must finish its game rule.");
                Assert.That(market.GetFactor(UnrelatedKey), Is.EqualTo(1.25));
            });

            var changes = component.Changes.ToDictionary(entry => entry.Key, entry => entry.Value);
            var groups = entities.System<MarketCommodityGroupSystem>();
            foreach (var (key, adjustment) in changes)
            {
                var oldFactor = before.TryGetValue(key, out var previous) ? previous.Factor : 1;
                var ratio = adjustment.NewFactor / oldFactor;
                Assert.Multiple(() =>
                {
                    Assert.That(adjustment.Name, Is.Not.Null.And.Not.Empty, key);
                    Assert.That(adjustment.OldFactor, Is.EqualTo(oldFactor), key);
                    Assert.That(ratio, Is.EqualTo(1 + change).Within(0.000000001)
                        .Or.EqualTo(1 - change).Within(0.000000001), key);
                    Assert.That(market.GetFactor(key), Is.EqualTo(adjustment.NewFactor), key);
                    Assert.That(groups.GetGroup(key).Id,
                        Is.AnyOf("RawMaterials", "HighPrecision", "UltraPrecision", "Gases"), key);
                });
            }

            foreach (var (key, quote) in market.GetAllQuotes())
            {
                if (changes.ContainsKey(key))
                    continue;

                Assert.That(before.TryGetValue(key, out var previous), Is.True, $"Unexpected new quote {key}.");
                Assert.That(quote, Is.EqualTo(previous), $"Unselected commodity {key} changed.");
            }
            foreach (var key in before.Keys)
                Assert.That(market.GetAllQuotes().ContainsKey(key), Is.True, $"Existing quote {key} disappeared.");
        });
    }

    [Test]
    public async Task GasOnlySelectionCanChangeTenCommoditiesWithinTenAttempts()
    {
        await RunTest((entities, ticker, market, configuration, rule) =>
        {
            var component = entities.GetComponent<MarketPriceShockRuleComponent>(rule);
#pragma warning disable RA0002 // Configure only this rule instance's eligible commodity groups.
            component.AllowedGroups = new() { "Gases" };
            component.ProductCount = 10;
            component.MaxSelectionAttempts = 10;
#pragma warning restore RA0002
            for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
                market.SetFactor(DynamicMarketSystem.GasKey(i), 1);

            Assert.That(ticker.StartGameRule(rule), Is.True);
            Assert.That(component.Changes.Count, Is.EqualTo(10),
                "Eligible gases must be filtered before the bounded random selection, not rejected after drawing general items.");
            var groups = entities.System<MarketCommodityGroupSystem>();
            foreach (var (key, change) in component.Changes)
            {
                Assert.That(key, Does.StartWith("gas:"));
                Assert.That(groups.GetGroup(key).Id, Is.EqualTo("Gases"), key);
                Assert.That(market.GetFactor(key), Is.EqualTo(change.NewFactor), key);
            }
        });
    }

    [Test]
    public async Task ChangingTheAllowedGroupsRebuildsThePoolForTheNextRule()
    {
        await RunTest((entities, ticker, market, configuration, rule) =>
        {
            var gasRule = entities.GetComponent<MarketPriceShockRuleComponent>(rule);
#pragma warning disable RA0002 // Warm the candidate cache using a gas-only rule instance.
            gasRule.AllowedGroups = new() { "Gases" };
            gasRule.ProductCount = 1;
            gasRule.MaxSelectionAttempts = 1;
#pragma warning restore RA0002
            for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
                market.SetFactor(DynamicMarketSystem.GasKey(i), 1);
            Assert.That(ticker.StartGameRule(rule), Is.True);
            Assert.That(gasRule.Changes.Count, Is.EqualTo(1));

            var rawRule = ticker.ForceAddGameRule(RuleId);
            try
            {
                entities.GetComponent<GameRuleComponent>(rawRule).Delay = null;
                var component = entities.GetComponent<MarketPriceShockRuleComponent>(rawRule);
#pragma warning disable RA0002 // The next event must replace the cached gas-only selection.
                component.AllowedGroups = new() { "RawMaterials" };
                component.ProductCount = 1;
#pragma warning restore RA0002
                Assert.That(ticker.StartGameRule(rawRule), Is.True);
                Assert.That(component.Changes.Count, Is.EqualTo(1));
                var groups = entities.System<MarketCommodityGroupSystem>();
                foreach (var (key, change) in component.Changes)
                {
                    Assert.That(groups.GetGroup(key).Id, Is.EqualTo("RawMaterials"), key);
                    Assert.That(market.GetFactor(key), Is.EqualTo(change.NewFactor), key);
                }
            }
            finally
            {
                ticker.EndGameRule(rawRule);
                entities.DeleteEntity(rawRule);
            }
        });
    }

    [Test]
    public async Task AnEmptyAllowedGroupSetCannotChangeAnyPrices()
    {
        await RunTest((entities, ticker, market, configuration, rule) =>
        {
            var component = entities.GetComponent<MarketPriceShockRuleComponent>(rule);
#pragma warning disable RA0002 // An explicitly empty allowlist must not mean unrestricted selection.
            component.AllowedGroups = new();
#pragma warning restore RA0002
            market.SetFactor(UnrelatedKey, 1.25);
            var before = market.GetAllQuotes().ToDictionary(entry => entry.Key, entry => entry.Value);

            Assert.That(ticker.StartGameRule(rule), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(component.Changes, Is.Empty);
                Assert.That(market.GetAllQuotes(), Is.EquivalentTo(before));
                Assert.That(entities.HasComponent<EndedGameRuleComponent>(rule), Is.True);
            });
        });
    }

    [TestCase(false, 256)]
    [TestCase(true, 0)]
    public async Task DisabledMarketOrAnEmptySelectionBudgetCannotPartiallyChangePrices(bool enabled, int attempts)
    {
        await RunTest((entities, ticker, market, configuration, rule) =>
        {
            var component = entities.GetComponent<MarketPriceShockRuleComponent>(rule);
#pragma warning disable RA0002 // Configure the rejected event without changing shared prototypes.
            component.MaxSelectionAttempts = attempts;
#pragma warning restore RA0002
            market.SetFactor(UnrelatedKey, 1.25);
            var before = market.GetAllQuotes().ToDictionary(entry => entry.Key, entry => entry.Value);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);

            Assert.That(ticker.StartGameRule(rule), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(component.Changes, Is.Empty);
                Assert.That(market.GetAllQuotes(), Is.EquivalentTo(before));
                Assert.That(entities.HasComponent<EndedGameRuleComponent>(rule), Is.True);
            });
        });
    }

    [Test]
    public async Task RepeatingTheStartedEventCannotApplyThePriceShockTwice()
    {
        await RunTest((entities, ticker, market, configuration, rule) =>
        {
            Assert.That(ticker.StartGameRule(rule), Is.True);
            var component = entities.GetComponent<MarketPriceShockRuleComponent>(rule);
            Assert.That(component.Changes.Count, Is.EqualTo(10));
            var changes = component.Changes.ToDictionary(entry => entry.Key, entry => entry.Value);
            var quotes = market.GetAllQuotes().ToDictionary(entry => entry.Key, entry => entry.Value);

            var started = new GameRuleStartedEvent(rule, RuleId);
            entities.EventBus.RaiseLocalEvent(rule, ref started);

            Assert.Multiple(() =>
            {
                Assert.That(component.Changes, Is.EquivalentTo(changes));
                Assert.That(market.GetAllQuotes(), Is.EquivalentTo(quotes));
            });
        });
    }

    [Test]
    public async Task ScalingRequiresTheFullChangeToFitTheMarketBoundsWithoutMutatingQuotes()
    {
        await RunTest((entities, ticker, market, configuration, rule) =>
        {
            configuration.SetCVar(EXCVars.DynamicMarketMinFactor, 0.5f);
            configuration.SetCVar(EXCVars.DynamicMarketMaxFactor, 2f);
            market.SetFactor(UnrelatedKey, 1.5);
            var before = market.GetAllQuotes()[UnrelatedKey];

            Assert.Multiple(() =>
            {
                Assert.That(market.TryGetScaledFactor(UnrelatedKey, 1.5, out var rejected), Is.False,
                    "A target of 2.25 must be rejected, not clamped to 2.");
                Assert.That(market.TryGetScaledFactor(UnrelatedKey, 0.5, out var accepted), Is.True);
                Assert.That(accepted, Is.EqualTo(0.75));
                Assert.That(market.TryGetScaledFactor(UnrelatedKey, 1, out var unchanged), Is.False);
                Assert.That(market.TryGetScaledFactor(UnrelatedKey, 0, out var zero), Is.False);
                Assert.That(market.TryGetScaledFactor(UnrelatedKey, double.NaN, out var invalid), Is.False);
                Assert.That(market.GetAllQuotes()[UnrelatedKey], Is.EqualTo(before));
            });

            market.SetFactor(UnrelatedKey, 0.75);
            before = market.GetAllQuotes()[UnrelatedKey];
            Assert.That(market.TryGetScaledFactor(UnrelatedKey, 0.5, out var belowFloor), Is.False,
                "A target of 0.375 must be rejected, not clamped to 0.5.");
            Assert.That(market.GetAllQuotes()[UnrelatedKey], Is.EqualTo(before));
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            Assert.That(market.TryGetScaledFactor(UnrelatedKey, 1.5, out var disabled), Is.False);
            Assert.That(market.GetAllQuotes()[UnrelatedKey], Is.EqualTo(before));
        });
    }

    private static async Task RunTest(
        Action<IEntityManager, GameTicker, DynamicMarketSystem, IConfigurationManager, EntityUid> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var ticker = server.System<GameTicker>();
            var market = server.System<DynamicMarketSystem>();
            var configuration = server.ResolveDependency<IConfigurationManager>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
            var minFactor = configuration.GetCVar(EXCVars.DynamicMarketMinFactor);
            var maxFactor = configuration.GetCVar(EXCVars.DynamicMarketMaxFactor);
            var decay = configuration.GetCVar(EXCVars.DynamicMarketDecayRate);
            var quotes = market.GetAllQuotes().ToDictionary(entry => entry.Key, entry => entry.Value);
            var history = (IList<(TimeSpan, string)>) ticker.AllPreviousGameRules;
            var previousHistory = history.ToArray();
            EntityUid? createdRule = null;
            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            configuration.SetCVar(EXCVars.DynamicMarketMinFactor, 0.01f);
            configuration.SetCVar(EXCVars.DynamicMarketMaxFactor, 9.99f);
            configuration.SetCVar(EXCVars.DynamicMarketDecayRate, 0f);
            try
            {
                var rule = ticker.ForceAddGameRule(RuleId);
                createdRule = rule;
                entities.GetComponent<GameRuleComponent>(rule).Delay = null;
                assertion(entities, ticker, market, configuration, rule);
            }
            finally
            {
                if (createdRule is { } rule && entities.EntityExists(rule))
                {
                    ticker.EndGameRule(rule);
                    entities.DeleteEntity(rule);
                }
                history.Clear();
                foreach (var entry in previousHistory)
                    history.Add(entry);

                configuration.SetCVar(EXCVars.DynamicMarketMinFactor, minFactor);
                configuration.SetCVar(EXCVars.DynamicMarketMaxFactor, maxFactor);
                foreach (var key in market.GetAllQuotes().Keys.ToArray())
                {
                    if (!quotes.ContainsKey(key))
                        market.ResetKey(key);
                }
                foreach (var (key, quote) in quotes)
                {
                    if (market.GetAllQuotes().TryGetValue(key, out var current) && current.Equals(quote))
                        continue;

                    market.SetFactor(key, quote.PreviousFactor);
                    market.SetFactor(key, quote.Factor);
                }
                configuration.SetCVar(EXCVars.DynamicMarketDecayRate, decay);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}
