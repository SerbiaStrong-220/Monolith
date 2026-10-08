// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Systems;
using Content.Server.Station.Systems;
using Content.Shared._Exodus.CCVar;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Cargo;
using Content.Shared.Cargo.BUI;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Events;
using Content.Shared.Station.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketReagentBoundaryTest
{
    private const string Carrier = "ExodusReagentBoundaryCarrier";
    private const string FlavorolKey = "reagent:Flavorol";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusReagentBoundaryConsole
          components:
          - type: CargoPalletConsole
          - type: MarketModifier
            buy: false
            mod: 1
          - type: UserInterface
            interfaces:
              enum.CargoPalletConsoleUiKey.Sale:
                type: CargoPalletConsoleBoundUserInterface

        - type: entity
          id: ExodusReagentBoundaryStation
          parent: BaseStationCargoMarket
          components:
          - type: StationTracker
          - type: StationData

        - type: entity
          id: ExodusReagentBoundaryCarrier
          parent: BaseItem
          components:
          - type: StaticPrice
            price: 10
          - type: IgnoreMarketModifier
          - type: MarketReagentPriceOverride
          - type: SolutionContainerManager
            solutions:
              chemicals:
                maxVol: 10
                reagents:
                - ReagentId: Flavorol
                  Quantity: 10

        - type: entity
          id: ExodusReagentBoundaryWater
          parent: BaseItem
          components:
          - type: StaticPrice
            price: 10
          - type: SolutionContainerManager
            solutions:
              chemicals:
                maxVol: 1000
                reagents:
                - ReagentId: Water
                  Quantity: 1000
        """;

    private sealed class ReagentPriceOverrideSystem : EntitySystem
    {
        public EntityUid? Target;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<MarketReagentPriceOverrideComponent, PriceCalculationEvent>(OnPrice);
        }

        private void OnPrice(Entity<MarketReagentPriceOverrideComponent> ent, ref PriceCalculationEvent args)
        {
            if (ent.Owner != Target)
                return;

            args.Price = 17;
            args.Handled = true;
        }
    }

    [Test]
    public async Task PalletKeepsItsReagentModifierWhenTheRootCarrierIsExempt()
    {
        await RunTest((entities, console, coordinates, market) =>
        {
            var uid = entities.SpawnEntity(Carrier, coordinates);
            var expected = new MarketTransactionState();
            var shellValue = market.CalculateSequentialSellValue($"proto:{Carrier}", 10, 1, 1, 1, expected, false);
            var reagentValue = market.CalculateSequentialSellValue(FlavorolKey, 10, 10, 1, 2, expected, false);
            var payout = DynamicMarketSystem.RoundSellPayout(shellValue + reagentValue);

            var first = Appraise(entities, console);
            var second = Appraise(entities, console);
            Assert.Multiple(() =>
            {
                Assert.That(first.Appraisal, Is.EqualTo(payout),
                    "The shell ignores the console's x2 modifier, but its transferable reagents keep that modifier.");
                Assert.That(first.Count, Is.EqualTo(1));
                Assert.That(first.Items, Has.Count.EqualTo(1));
                Assert.That(first.Items![0].Price, Is.EqualTo(payout));
                Assert.That(second.Appraisal, Is.EqualTo(payout));
                Assert.That(market.GetFactor(FlavorolKey), Is.EqualTo(1), "Repeated appraisals must not apply pressure.");
            });

            entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
            Assert.That(entities.Deleted(uid), Is.True);
            Assert.That(market.GetFactor(FlavorolKey), Is.EqualTo(expected.Factors[FlavorolKey]).Within(1e-12),
                "The actual sale must apply pressure for all ten units exactly once.");
        });
    }

    [Test]
    public async Task BuyingZeroPriceWaterDoesNotRaiseItsReagentFactor()
    {
        await RunTest((entities, console, coordinates, market) =>
        {
            const string prototype = "ExodusReagentBoundaryWater";
            const string key = "reagent:Water";
            var purchases = entities.System<MarketPurchaseSystem>();
            Assert.That(purchases.TryQuotePrototypeBuy(prototype, 1, 10, 1, out var quote,
                ceiling: new MarketSellCeiling(1, 1)), Is.True);
            Assert.That(quote.Transaction.Factors.ContainsKey(key), Is.False,
                "A reagent without a positive commodity price must not gain demand from buying its carrier.");
            market.CommitTransaction(quote.Transaction);
            Assert.Multiple(() =>
            {
                Assert.That(market.GetFactor(key), Is.EqualTo(1));
                Assert.That(market.GetFactor($"proto:{prototype}"), Is.GreaterThan(1),
                    "The real shell purchase must still create ordinary item demand.");
            });
        });
    }

    [Test]
    public async Task FreeCanonicalStackChemistryDoesNotMakeItsDeliveryInexact()
    {
        await RunTest((entities, console, coordinates, market) =>
        {
            const string prototype = "MaterialDurathread";
            const string key = "stack:Durathread";
            var baskets = entities.System<MarketBasketSystem>();
            Assert.That(baskets.TryGetPrototypeBasket(prototype, out var basket, out var failure), Is.True, failure);
            Assert.That(basket.Exact, Is.True,
                "A canonical split's extra zero-price Fiber does not change the priced commodity delivery.");
            Assert.That(basket.Lines, Has.Count.EqualTo(1));
            var purchases = entities.System<MarketPurchaseSystem>();
            Assert.That(purchases.TryQuotePrototypeBuy(prototype, 1, basket.NominalValue, 1, out var quote,
                ceiling: new MarketSellCeiling(1, 1)), Is.True);
            Assert.That(quote.Transaction.Factors.ContainsKey(key), Is.True);
            Assert.That(quote.Transaction.Factors.ContainsKey("reagent:Fiber"), Is.False);
            market.CommitTransaction(quote.Transaction);
            Assert.That(market.GetFactor(key), Is.GreaterThan(1));
        });
    }

    [Test]
    public async Task OpaqueHandledPriceCannotHideAValuableSolutionInItsCarrierKey()
    {
        await RunTest((entities, console, coordinates, market) =>
        {
            var uid = entities.SpawnEntity(Carrier, coordinates);
            var overrides = entities.System<ReagentPriceOverrideSystem>();
            overrides.Target = uid;
            try
            {
                var baskets = entities.System<MarketBasketSystem>();
                Assert.That(baskets.TryGetEntityOwnBasket(uid, out var own, out var includesContents, out var failure), Is.False);
                Assert.That(failure, Is.Not.Null.And.Not.Empty);
                Assert.That(includesContents, Is.False);
                Assert.That(own.Lines, Is.Empty);
                Assert.That(baskets.TryGetEntityBasket(uid, out var recursive, out failure), Is.False);
                Assert.That(failure, Is.Not.Null.And.Not.Empty);
                Assert.That(recursive.Lines, Is.Empty);

                var appraisal = Appraise(entities, console);
                Assert.That(appraisal.Count, Is.Zero);
                Assert.That(appraisal.Appraisal, Is.Zero);
                entities.EventBus.RaiseLocalEvent(console, new CargoPalletSellMessage());
                Assert.That(entities.Deleted(uid), Is.False, "A rejected appraisal must leave both the carrier and solution intact.");
                Assert.That(market.GetFactor(FlavorolKey), Is.EqualTo(1));
            }
            finally
            {
                overrides.Target = null;
            }
        });
    }

    private static CargoPalletConsoleInterfaceState Appraise(IEntityManager entities, EntityUid console)
    {
        entities.EventBus.RaiseLocalEvent(console, new CargoPalletAppraiseMessage());
        Assert.That(entities.System<UserInterfaceSystem>().TryGetUiState<CargoPalletConsoleInterfaceState>(console,
            CargoPalletConsoleUiKey.Sale, out var state), Is.True);
        return state!;
    }

    private static async Task RunTest(Action<IEntityManager, EntityUid, EntityCoordinates, DynamicMarketSystem> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var configuration = pair.Server.ResolveDependency<IConfigurationManager>();
            var market = entities.System<DynamicMarketSystem>();
            var inventory = entities.System<MarketInventorySystem>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
            var sellImpact = configuration.GetCVar(EXCVars.DynamicMarketSellImpact);
            var buyImpact = configuration.GetCVar(EXCVars.DynamicMarketBuyImpact);
            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            configuration.SetCVar(EXCVars.DynamicMarketSellImpact, 0.08f);
            configuration.SetCVar(EXCVars.DynamicMarketBuyImpact, 0.08f);
            market.ResetAll();
            inventory.Clear();
            try
            {
                var maps = entities.System<SharedMapSystem>();
                for (var x = 0; x <= 2; x++)
                {
                    for (var y = -2; y <= 0; y++)
                        maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                }

                var station = entities.SpawnEntity("ExodusReagentBoundaryStation", new EntityCoordinates(map.Grid, 2.5f, -1.5f));
                var stations = entities.System<StationSystem>();
                stations.SetStation(station, station);
                stations.AddGridToStation(station, map.Grid);
                var coordinates = new EntityCoordinates(map.Grid, 0.5f, 0.5f);
                var pallet = entities.SpawnEntity("CargoPalletSell", coordinates);
                Assert.That(entities.GetComponent<TransformComponent>(pallet).Anchored, Is.True);
                var console = entities.SpawnEntity("ExodusReagentBoundaryConsole", new EntityCoordinates(map.Grid, 2.5f, 0.5f));
                // The high test rate must not alter the prototype ceiling for unrelated economy fixtures.
                entities.GetComponent<MarketModifierComponent>(console).Mod = 2;
                assertion(entities, console, coordinates, market);
            }
            finally
            {
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                market.ResetAll();
                inventory.Clear();
                configuration.SetCVar(EXCVars.DynamicMarketSellImpact, sellImpact);
                configuration.SetCVar(EXCVars.DynamicMarketBuyImpact, buyImpact);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }
}

[RegisterComponent]
public sealed partial class MarketReagentPriceOverrideComponent : Component;
