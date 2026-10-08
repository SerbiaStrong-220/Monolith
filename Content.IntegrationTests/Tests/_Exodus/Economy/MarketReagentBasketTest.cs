// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.Linq;
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Systems;
using Content.Server.Stack;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketReagentBasketTest
{
    private const string FlavorolKey = "reagent:Flavorol";
    private const string OtherKey = "reagent:ExodusMarketReagent";
    private const string Filled = "ExodusMarketReagentFilled";
    private const string Empty = "ExodusMarketReagentEmpty";

    [TestPrototypes]
    private const string Prototypes = """
        - type: reagent
          id: ExodusMarketReagent
          parent: Water
          pricePerUnit: 7

        - type: entity
          id: ExodusMarketReagentEmpty
          components:
          - type: Item
          - type: StaticPrice
            price: 10
          - type: SolutionContainerManager
            solutions:
              beaker:
                maxVol: 100
              reserve:
                maxVol: 100

        - type: entity
          id: ExodusMarketReagentFilled
          parent: ExodusMarketReagentEmpty
          components:
          - type: SolutionContainerManager
            solutions:
              beaker:
                maxVol: 100
                reagents:
                - ReagentId: Flavorol
                  Quantity: 2.12
                - ReagentId: Water
                  Quantity: 20.5
              reserve:
                maxVol: 100
                reagents:
                - ReagentId: Flavorol
                  Quantity: 0.38
                - ReagentId: ExodusMarketReagent
                  Quantity: 0.25

        - type: entity
          id: ExodusMarketReagentTaxed
          parent: ExodusMarketReagentFilled
          components:
          - type: ItemTax
            taxAccounts:
              Medical: 0.8

        - type: entity
          id: ExodusMarketReagentIgnored
          parent: ExodusMarketReagentTaxed
          components:
          - type: IgnoreMarketModifier
        """;

    [Test]
    public async Task PrototypeAndRuntimePreserveFractionalMixturesAndSeparateTheEmptyShell()
    {
        await RunTest((entities, coordinates, market) =>
        {
            var baskets = entities.System<MarketBasketSystem>();
            var filled = entities.SpawnEntity(Filled, coordinates);
            var empty = entities.SpawnEntity(Empty, coordinates);
            Assert.That(baskets.TryGetPrototypeBasket(Filled, out var prototype, out var failure), Is.True, failure);
            Assert.That(baskets.TryGetEntityBasket(filled, out var runtime, out failure), Is.True, failure);
            Assert.That(baskets.TryGetEntityOwnBasket(filled, out var own, out var includesContents, out failure), Is.True, failure);
            Assert.That(baskets.TryGetEntityBasket(empty, out var emptyBasket, out failure), Is.True, failure);

            foreach (var basket in new[] { prototype, runtime, own })
            {
                Assert.Multiple(() =>
                {
                    Assert.That(basket.Exact, Is.True);
                    Assert.That(Quantity(basket, FlavorolKey), Is.EqualTo(2.5));
                    Assert.That(Quantity(basket, OtherKey), Is.EqualTo(0.25));
                    Assert.That(basket.Lines.Where(line => line.MarketKey == FlavorolKey)
                        .All(line => line.UnitBasePrice == 10), Is.True);
                    Assert.That(basket.Lines.Where(line => line.MarketKey == OtherKey)
                        .All(line => line.UnitBasePrice == 7), Is.True);
                    Assert.That(basket.Lines.Where(line => line.MarketKey == "reagent:Water")
                        .All(line => line.UnitBasePrice == 0), Is.True);
                    Assert.That(basket.Lines.Single(line => line.MarketKey == $"proto:{Filled}").UnitBasePrice,
                        Is.EqualTo(emptyBasket.NominalValue));
                    Assert.That(basket.NominalValue, Is.EqualTo(36.75).Within(1e-9));
                });
            }

            Assert.That(includesContents, Is.False, "Solution entities must not claim ownership of unrelated contained items.");
            Assert.That(entities.System<PricingSystem>().GetPrice(filled), Is.EqualTo(runtime.NominalValue).Within(1e-9),
                "Extracting commodity lines must neither lose nor count the reagent price twice.");
        });
    }

    [TestCase(4000)]
    [TestCase(1000000)]
    public async Task ChemMasterAndNestedSplitJugsHaveTheSameReagentValueAndPressure(int amount)
    {
        await RunTest((entities, coordinates, market) =>
        {
            var baskets = entities.System<MarketBasketSystem>();
            var solutions = entities.System<SharedSolutionContainerSystem>();
            var containers = entities.System<SharedContainerSystem>();
            var machine = entities.SpawnEntity("ChemMaster", coordinates);
            Assert.That(baskets.TryGetEntityBasket(machine, out var emptyMachine, out var failure), Is.True, failure);
            Fill(entities, machine, SharedChemMaster.BufferSolutionName, "Flavorol", amount);
            Assert.That(baskets.TryGetEntityBasket(machine, out var fullMachine, out failure), Is.True, failure);
            Assert.That(fullMachine.NominalValue - emptyMachine.NominalValue, Is.EqualTo(amount * 10.0).Within(1e-6));
            Assert.That(fullMachine.Lines.Single(line => line.MarketKey == "proto:ChemMaster").UnitBasePrice,
                Is.EqualTo(emptyMachine.Lines.Single(line => line.MarketKey == "proto:ChemMaster").UnitBasePrice).Within(1e-6));

            var outer = entities.SpawnEntity(Empty, coordinates);
            var inner = entities.SpawnEntity(Empty, coordinates);
            Assert.That(containers.Insert(inner, containers.EnsureContainer<Container>(outer, "test-cargo")), Is.True);
            var cargo = containers.EnsureContainer<Container>(inner, "test-cargo");
            const int jugCount = 20;
            for (var i = 0; i < jugCount; i++)
            {
                var jug = entities.SpawnEntity("Jug", coordinates);
                Fill(entities, jug, "beaker", "Flavorol", amount / jugCount);
                Assert.That(containers.Insert(jug, cargo), Is.True);
            }

            Assert.That(baskets.TryGetEntityBasket(outer, out var split, out failure), Is.True, failure);
            Assert.That(baskets.TryGetEntityOwnBasket(outer, out var own, out var includesContents, out failure), Is.True, failure);
            Assert.That(includesContents, Is.False);
            Assert.That(Quantity(own, FlavorolKey), Is.Zero, "The wrapper's own basket must leave the jugs to traversal.");
            Assert.That(Quantity(fullMachine, FlavorolKey), Is.EqualTo(amount));
            Assert.That(Quantity(split, FlavorolKey), Is.EqualTo(amount));

            var bulkTransaction = new MarketTransactionState();
            var splitTransaction = new MarketTransactionState();
            var bulkValue = Sell(market, Reagents(fullMachine), bulkTransaction);
            var splitValue = Sell(market, Reagents(split), splitTransaction);
            Assert.Multiple(() =>
            {
                Assert.That(bulkValue, Is.GreaterThan(0));
                Assert.That(splitValue, Is.EqualTo(bulkValue).Within(1e-6));
                Assert.That(splitTransaction.Factors[FlavorolKey], Is.EqualTo(bulkTransaction.Factors[FlavorolKey]).Within(1e-12));
                Assert.That(bulkTransaction.Factors[FlavorolKey], Is.LessThan(1));
                Assert.That(market.GetFactor(FlavorolKey), Is.EqualTo(1), "Appraisal must not commit market pressure.");
            });
            Assert.That(solutions.TryGetSolution(machine, SharedChemMaster.BufferSolutionName, out _, out var remaining), Is.True);
            Assert.That(remaining.Volume, Is.EqualTo(FixedPoint2.New(amount)), "Appraisal must not consume the solution.");
        });
    }

    [TestCase("ExodusMarketReagentTaxed", false)]
    [TestCase("ExodusMarketReagentIgnored", true)]
    public async Task CarrierTaxAndMarketExemptionApplyOnlyToItsShell(string prototypeId, bool ignored)
    {
        await RunTest((entities, coordinates, market) =>
        {
            var baskets = entities.System<MarketBasketSystem>();
            var uid = entities.SpawnEntity(prototypeId, coordinates);
            Assert.That(baskets.TryGetPrototypeBasket(prototypeId, out var prototype, out var failure), Is.True, failure);
            Assert.That(baskets.TryGetEntityOwnBasket(uid, out var runtime, out _, out failure), Is.True, failure);
            foreach (var basket in new[] { prototype, runtime })
            {
                var shell = basket.Lines.Single(line => line.MarketKey == $"proto:{prototypeId}");
                Assert.That(shell.Tax.Medical, Is.EqualTo(0.8).Within(1e-7));
                Assert.That(shell.IgnoreMarketModifier, Is.EqualTo(ignored));
                Assert.That(Quantity(basket, FlavorolKey), Is.EqualTo(2.5));
                foreach (var line in Reagents(basket))
                {
                    Assert.That(line.IgnoreMarketModifier, Is.False, "Pouring into an exempt carrier must not bypass reagent pressure.");
                    Assert.That(line.Tax, Is.EqualTo(default(MarketItemTax)), "Pouring must not attach the carrier's tax to the reagent.");
                }
            }
        });
    }

    [TestCase(Filled)]
    [TestCase("ExodusMarketReagentIgnored")]
    public async Task BuyingFilledThenSellingItsShellAndTransferredReagentsCannotProfit(string prototypeId)
    {
        await RunTest((entities, coordinates, market) =>
        {
            market.SetFactor(FlavorolKey, 3);
            market.SetFactor(OtherKey, 2);
            var purchases = entities.System<MarketPurchaseSystem>();
            Assert.That(purchases.TryQuotePrototypeBuy(prototypeId, 1, 1, 1, out var purchase,
                ceiling: new MarketSellCeiling(1, 1)), Is.True);
            Assert.That(purchase.Transaction.Factors.ContainsKey(FlavorolKey), Is.True,
                "Purchasing a filled container must raise the same reagent market later used for sale.");
            market.CommitTransaction(purchase.Transaction);

            var purchased = entities.SpawnEntity(prototypeId, coordinates);
            var receiver = entities.SpawnEntity(Empty, coordinates);
            var solutions = entities.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(receiver, "beaker", out var target), Is.True);
            foreach (var name in new[] { "beaker", "reserve" })
            {
                Assert.That(solutions.TryGetSolution(purchased, name, out var solution, out var source), Is.True);
                var transferred = solutions.SplitSolution(solution.Value, source.Volume);
                Assert.That(solutions.TryAddSolution(target.Value, transferred), Is.True);
            }

            var baskets = entities.System<MarketBasketSystem>();
            Assert.That(baskets.TryGetEntityOwnBasket(purchased, out var shell, out _, out var failure), Is.True, failure);
            Assert.That(baskets.TryGetEntityOwnBasket(receiver, out var contents, out _, out failure), Is.True, failure);
            Assert.That(Quantity(shell, FlavorolKey), Is.Zero);
            Assert.That(Quantity(contents, FlavorolKey), Is.EqualTo(2.5));
            Assert.That(Quantity(contents, OtherKey), Is.EqualTo(0.25));
            var transaction = new MarketTransactionState();
            var payout = Sell(market, shell.Lines, transaction) + Sell(market, Reagents(contents), transaction);
            Assert.That(DynamicMarketSystem.RoundSellPayout(payout), Is.LessThanOrEqualTo(purchase.TotalPrice),
                "The purchase floor must cover extraction, carrier exemptions and the shell's own tax.");
        });
    }

    [TestCase("MaterialBananium")]
    [TestCase("FoodBakedPancake")]
    public async Task PricedStacksRemainPurchasableAndBoundTheirSplitChemistry(string prototypeId)
    {
        await RunTest((entities, coordinates, market) =>
        {
            var baskets = entities.System<MarketBasketSystem>();
            Assert.That(baskets.TryGetPrototypeBasket(prototypeId, out var bound, out var failure), Is.True, failure);
            var purchased = entities.SpawnEntity(prototypeId, coordinates);
            var stack = entities.GetComponent<StackComponent>(purchased);
            var count = stack.Count;
            Assert.That(count, Is.InRange(1, 100));
            Assert.That(baskets.TryGetEntityOwnBasket(purchased, out var original, out _, out failure), Is.True, failure);
            var purchases = entities.System<MarketPurchaseSystem>();
            Assert.That(purchases.TryQuotePrototypeBuy(prototypeId, 1, 1, 1, out var quote,
                ceiling: new MarketSellCeiling(1, 1)), Is.True);
            foreach (var reagent in Reagents(bound))
            {
                if (reagent.Quantity <= Quantity(original, reagent.MarketKey))
                    continue;
                Assert.That(bound.Exact, Is.False);
                Assert.That(quote.Transaction.Factors.ContainsKey(reagent.MarketKey), Is.False,
                    "A conservative bound must not create demand for chemicals only regenerated by splitting.");
            }
            market.CommitTransaction(quote.Transaction);

            var units = new List<EntityUid> { purchased };
            var stacks = entities.System<StackSystem>();
            for (var i = 1; i < count; i++)
            {
                var split = stacks.Split(purchased, 1, coordinates);
                Assert.That(split, Is.Not.Null);
                units.Add(split.Value);
            }

            var receiver = entities.SpawnEntity(Empty, coordinates);
            var solutions = entities.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(receiver, "beaker", out var target), Is.True);
            solutions.SetCapacity(target.Value, FixedPoint2.New(1000));
            var actualAmounts = new Dictionary<string, double>();
            var transaction = new MarketTransactionState();
            double payout = 0;
            foreach (var unit in units)
            {
                Assert.That(entities.GetComponent<StackComponent>(unit).Count, Is.EqualTo(1));
                Assert.That(baskets.TryGetEntityOwnBasket(unit, out var actual, out _, out failure), Is.True, failure);
                foreach (var line in Reagents(actual))
                    actualAmounts[line.MarketKey] = actualAmounts.GetValueOrDefault(line.MarketKey) + line.Quantity;

                Assert.That(solutions.TryGetSolution(unit, "food", out var sourceEntity, out var source), Is.True);
                var extracted = solutions.SplitSolution(sourceEntity.Value, source.Volume);
                Assert.That(solutions.TryAddSolution(target.Value, extracted), Is.True);
                Assert.That(baskets.TryGetEntityOwnBasket(unit, out var shell, out _, out failure), Is.True, failure);
                Assert.That(Reagents(shell), Is.Empty);
                payout += Sell(market, shell.Lines, transaction);
            }

            Assert.That(actualAmounts, Is.Not.Empty);
            Assert.That(baskets.TryGetEntityOwnBasket(receiver, out var liquids, out _, out failure), Is.True, failure);
            foreach (var (key, amount) in actualAmounts)
            {
                Assert.That(Quantity(bound, key), Is.GreaterThanOrEqualTo(amount), key);
                Assert.That(Quantity(liquids, key), Is.EqualTo(amount), key);
            }
            payout += Sell(market, Reagents(liquids), transaction);
            Assert.That(DynamicMarketSystem.RoundSellPayout(payout), Is.LessThanOrEqualTo(quote.TotalPrice),
                "Buying a priced stack must cover its split shells and all extractable split chemistry.");
        });
    }

    private static double Quantity(MarketBasket basket, string key)
    {
        return basket.Lines.Where(line => line.MarketKey == key).Sum(line => line.Quantity);
    }

    private static IEnumerable<MarketBasketLine> Reagents(MarketBasket basket)
    {
        return basket.Lines.Where(line => line.MarketKey.StartsWith("reagent:", StringComparison.Ordinal));
    }

    private static double Sell(DynamicMarketSystem market, IEnumerable<MarketBasketLine> lines, MarketTransactionState transaction)
    {
        double total = 0;
        foreach (var line in lines)
        {
            var value = market.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice, line.Quantity,
                1, 1, transaction, applyImpact: false);
            total += value * line.Tax.PositiveMultiplier;
        }
        return total;
    }

    private static void Fill(IEntityManager entities, EntityUid uid, string name, string reagent, double amount)
    {
        var solutions = entities.System<SharedSolutionContainerSystem>();
        Assert.That(solutions.TryGetSolution(uid, name, out var solution), Is.True);
        var quantity = FixedPoint2.New(amount);
        solutions.SetCapacity(solution.Value, quantity);
        Assert.That(solutions.TryAddReagent(solution.Value, reagent, quantity), Is.True);
    }

    private static async Task RunTest(Action<IEntityManager, EntityCoordinates, DynamicMarketSystem> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var configuration = pair.Server.ResolveDependency<IConfigurationManager>();
            var market = entities.System<DynamicMarketSystem>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
            var definitions = new[]
            {
                EXCVars.DynamicMarketSellImpact,
                EXCVars.DynamicMarketBuyImpact,
                EXCVars.DynamicMarketReferenceVolume,
                EXCVars.DynamicMarketMinFactor,
                EXCVars.DynamicMarketMaxFactor,
                EXCVars.MarketPurchaseMargin,
            };
            var previous = definitions.Select(definition => configuration.GetCVar(definition)).ToArray();
            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            foreach (var definition in definitions)
                configuration.SetCVar(definition, definition.DefaultValue);
            market.ResetAll();
            try
            {
                assertion(entities, map.GridCoords, market);
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
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
