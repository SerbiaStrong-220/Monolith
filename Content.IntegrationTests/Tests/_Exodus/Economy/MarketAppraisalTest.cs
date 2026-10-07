// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.Reflection;
using System.Threading.Tasks;
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Systems;
using Content.Server.CartridgeLoader;
using Content.Server.CartridgeLoader.Cartridges;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Interaction;
using Robust.Shared.Configuration;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketAppraisalTest
{
    private const string ItemKey = "proto:ExodusAppraisalItem";
    private const string StackKey = "stack:ExodusAppraisalStack";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusAppraisalItem
          components:
          - type: Item
          - type: StaticPrice
            price: 100

        - type: entity
          id: ExodusAppraisalContainer
          components:
          - type: StaticPrice
            price: 5
          - type: ContainerContainer
            containers:
              contents: !type:Container

        - type: entity
          id: ExodusAppraisalCheapStack
          components:
          - type: Item
          - type: Stack
            stackType: ExodusAppraisalStack
            count: 50
          - type: StackPrice
            price: 10

        - type: entity
          id: ExodusAppraisalExpensiveStack
          parent: ExodusAppraisalCheapStack
          components:
          - type: StackPrice
            price: 100

        - type: stack
          id: ExodusAppraisalStack
          name: stack-steel
          spawn: ExodusAppraisalCheapStack
          maxCount: 100
        """;

    [Test]
    public async Task CurrentQuoteReplacesNominalPriceWithoutChangingTheMarket()
    {
        await RunTest((entities, coordinates, market, appraisal, _) =>
        {
            var item = entities.SpawnEntity("ExodusAppraisalItem", coordinates);
            var nominal = entities.System<PricingSystem>().GetPrice(item);
            market.SetFactor(ItemKey, 2);
            var expected = market.CalculateSequentialSellValue(ItemKey, nominal, 1, 1, 1, null, false);
            var quoteCount = market.GetAllQuotes().Count;

            for (var i = 0; i < 3; i++)
            {
                Assert.That(appraisal.TryGetEntitySellPrice(item, out var price), Is.True);
                Assert.That(price, Is.EqualTo(expected).Within(1e-10));
                Assert.That(price, Is.GreaterThan(nominal));
            }

            Assert.That(market.GetFactor(ItemKey), Is.EqualTo(2), "Repeated appraisal must not consume market pressure.");

            market.SetFactor(ItemKey, 0.5);
            Assert.That(appraisal.TryGetEntitySellPrice(item, out var reduced), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reduced, Is.LessThan(nominal));
                Assert.That(market.GetFactor(ItemKey), Is.EqualTo(0.5));
                Assert.That(market.GetAllQuotes(), Has.Count.EqualTo(quoteCount));
            });
        });
    }

    [Test]
    public async Task PdaHistoryUsesTheMarketQuoteAndRejectsScansDuringCooldown()
    {
        await RunTest((entities, coordinates, market, _, _) =>
        {
            var loader = entities.SpawnEntity("CaptainPDA", coordinates);
            var cartridges = entities.System<CartridgeLoaderSystem>();
            Assert.That(cartridges.InstallProgram(loader, "AppraisalCartridge"), Is.True);
            Assert.That(cartridges.TryGetProgram<AppraisalCartridgeComponent>(loader, out var program,
                out var cartridge, installedOnly: true), Is.True);
            cartridges.ActivateProgram(loader, program!.Value);
            var item = entities.SpawnEntity("ExodusAppraisalItem", coordinates);
            market.SetFactor(ItemKey, 2);
            var expected = DynamicMarketSystem.RoundSellPayout(
                market.CalculateSequentialSellValue(ItemKey, 100, 1, 1, 1, null, false));
            var scan = new AfterInteractEvent(loader, loader, item, coordinates, true);
            entities.EventBus.RaiseLocalEvent(loader, scan);

            Assert.That(scan.Handled, Is.True);
            Assert.That(cartridge!.AppraisedItems, Has.Count.EqualTo(1));
            Assert.That(cartridge.AppraisedItems[0].AppraisedPrice, Is.EqualTo(expected.ToString("0.00")));

            var repeatedScan = new AfterInteractEvent(loader, loader, item, coordinates, true);
            entities.EventBus.RaiseLocalEvent(loader, repeatedScan);
            Assert.Multiple(() =>
            {
                Assert.That(repeatedScan.Handled, Is.False);
                Assert.That(cartridge.AppraisedItems, Has.Count.EqualTo(1), "A rejected scan must not enter PDA history.");
                Assert.That(market.GetFactor(ItemKey), Is.EqualTo(2));
            });
        });
    }

    [Test]
    public async Task ContainerPricesSharedCommoditiesInSaleOrderWithOneTransaction()
    {
        await RunTest((entities, coordinates, market, appraisal, _) =>
        {
            var container = entities.SpawnEntity("ExodusAppraisalContainer", coordinates);
            var expensive = entities.SpawnEntity("ExodusAppraisalExpensiveStack", coordinates);
            var cheap = entities.SpawnEntity("ExodusAppraisalCheapStack", coordinates);
            var containers = entities.System<SharedContainerSystem>();
            var contents = containers.EnsureContainer<Container>(container, "contents");
            Assert.That(containers.Insert(expensive, contents), Is.True);
            Assert.That(containers.Insert(cheap, contents), Is.True);
            market.SetFactor(StackKey, 2);

            var transaction = new MarketTransactionState();
            var expected = market.CalculateSequentialSellValue("proto:ExodusAppraisalContainer", 5, 1, 1, 1,
                transaction, false);
            expected += market.CalculateSequentialSellValue(StackKey, 10, 50, 1, 1, transaction, false);
            expected += market.CalculateSequentialSellValue(StackKey, 100, 50, 1, 1, transaction, false);
            var independent = market.CalculateSequentialSellValue(StackKey, 10, 50, 1, 1, null, false)
                + market.CalculateSequentialSellValue(StackKey, 100, 50, 1, 1, null, false);
            var reversedTransaction = new MarketTransactionState();
            var reversed = market.CalculateSequentialSellValue(StackKey, 100, 50, 1, 1, reversedTransaction, false)
                + market.CalculateSequentialSellValue(StackKey, 10, 50, 1, 1, reversedTransaction, false);

            Assert.That(appraisal.TryGetEntitySellPrice(container, out var price), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(price, Is.EqualTo(expected).Within(1e-8));
                Assert.That(price, Is.LessThan(independent));
                Assert.That(price, Is.LessThan(reversed));
                Assert.That(market.GetFactor(StackKey), Is.EqualTo(2));
                Assert.That(market.GetAllQuotes(), Has.Count.EqualTo(1), "Preview must not create shell quotes.");
            });
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task StackQuantityIsIncludedWithAndWithoutTheDynamicMarket(bool enabled)
    {
        await RunTest((entities, coordinates, market, appraisal, configuration) =>
        {
            var stack = entities.SpawnEntity("ExodusAppraisalCheapStack", coordinates);
            market.SetFactor(StackKey, 2);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
            var expected = enabled
                ? market.CalculateSequentialSellValue(StackKey, 10, 50, 1, 1, null, false)
                : 500;

            Assert.That(appraisal.TryGetEntitySellPrice(stack, out var price), Is.True);
            Assert.That(price, Is.EqualTo(expected).Within(1e-8));
        });
    }

    [Test]
    public async Task InvalidEntitiesFailWithoutCreatingQuotes()
    {
        await RunTest((entities, coordinates, market, appraisal, _) =>
        {
            var item = entities.SpawnEntity("ExodusAppraisalItem", coordinates);
            entities.DeleteEntity(item);
            Assert.That(appraisal.TryGetEntitySellPrice(item, out var deletedPrice), Is.False);
            Assert.That(deletedPrice, Is.Zero);
            var unprototyped = entities.SpawnEntity(null, coordinates);
            Assert.That(appraisal.TryGetEntitySellPrice(unprototyped, out var unprototypedPrice), Is.False);
            Assert.That(unprototypedPrice, Is.Zero);
            Assert.That(market.GetAllQuotes(), Is.Empty);
        });
    }

    [Test]
    public async Task PendingSavedQuotesRejectAppraisal()
    {
        await RunTest((entities, coordinates, market, appraisal, _) =>
        {
            var item = entities.SpawnEntity("ExodusAppraisalItem", coordinates);
            var persistence = GetMarketField("_persist");
            var loaded = GetMarketField("_loadCompleted");
            var reset = GetMarketField("_blockLoadApply");
            var oldPersistence = persistence.GetValue(market);
            var oldLoaded = loaded.GetValue(market);
            var oldReset = reset.GetValue(market);
            try
            {
                persistence.SetValue(market, true);
                loaded.SetValue(market, false);
                reset.SetValue(market, false);
                Assert.That(market.Ready, Is.False);
                Assert.That(appraisal.TryGetEntitySellPrice(item, out var price), Is.False);
                Assert.That(price, Is.Zero);
                Assert.That(market.GetAllQuotes(), Is.Empty);
            }
            finally
            {
                persistence.SetValue(market, oldPersistence);
                loaded.SetValue(market, oldLoaded);
                reset.SetValue(market, oldReset);
            }
        });
    }

    private static FieldInfo GetMarketField(string name)
    {
        return typeof(DynamicMarketSystem).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    }

    private static async Task RunTest(
        Action<IEntityManager, EntityCoordinates, DynamicMarketSystem, MarketAppraisalSystem, IConfigurationManager> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();
        await PoolManager.WaitUntil(server, () => market.Ready);
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var configuration = server.ResolveDependency<IConfigurationManager>();
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
            try
            {
                configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
                for (var i = 0; i < definitions.Length; i++)
                    configuration.SetCVar(definitions[i], definitions[i].DefaultValue);
                configuration.SetCVar(EXCVars.DynamicMarketSellImpact, 0.8f);
                configuration.SetCVar(EXCVars.DynamicMarketBuyImpact, 0.8f);
                market.ResetAll();
                assertion(entities, map.GridCoords, market, entities.System<MarketAppraisalSystem>(), configuration);
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
