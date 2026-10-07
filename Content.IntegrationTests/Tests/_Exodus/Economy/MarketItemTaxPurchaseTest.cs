// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Components;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Mono.ItemTax.Components;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketItemTaxPurchaseTest
{
    private const string UntaxedItem = "ExodusItemTaxUntaxed";
    private const string MedicalItem = "ExodusItemTaxMedical";

    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusItemTaxUntaxed
          components:
          - type: Item
          - type: StaticPrice
            price: 100

        - type: entity
          id: ExodusItemTaxMedical
          parent: ExodusItemTaxUntaxed
          components:
          - type: ItemTax
            taxAccounts:
              Medical: 0.8

        - type: entity
          id: ExodusItemTaxMixedAccounts
          parent: ExodusItemTaxUntaxed
          components:
          - type: ItemTax
            taxAccounts:
              Medical: 0.5
              Nfsd: 0.3
              Frontier: -100
              Mieyo: 1000

        - type: entity
          id: ExodusItemTaxContainer
          parent: ExodusItemTaxUntaxed
          components:
          - type: StaticPrice
            price: 101
          - type: ItemTax
            taxAccounts:
              Frontier: 0.5
          - type: ContainerContainer
            containers:
              contents: !type:Container
          - type: ContainerFill
            containers:
              contents:
              - ExodusItemTaxUntaxed
              - ExodusItemTaxMedical

        - type: entity
          id: ExodusItemTaxUntaxedPackage
          components:
          - type: Item
          - type: ItemTax
            taxAccounts:
              Frontier: 0.5
          - type: SpawnItemsOnUse
            items:
            - id: ExodusItemTaxUntaxed

        - type: entity
          id: ExodusItemTaxMedicalPackage
          parent: ExodusItemTaxUntaxedPackage
          components:
          - type: SpawnItemsOnUse
            items:
            - id: ExodusItemTaxMedical

        - type: stack
          id: ExodusItemTaxSplitUnits
          name: stack-steel
          spawn: ExodusItemTaxSplitSingle
          maxCount: 50

        - type: entity
          id: ExodusItemTaxSplitStack
          components:
          - type: Item
          - type: Stack
            stackType: ExodusItemTaxSplitUnits
            count: 5
          - type: StackPrice
            price: 100

        - type: entity
          id: ExodusItemTaxSplitSingle
          parent: ExodusItemTaxSplitStack
          components:
          - type: Stack
            count: 1
          - type: ItemTax
            taxAccounts:
              Medical: 0.8

        - type: stack
          id: ExodusItemTaxMergeUnits
          name: stack-steel
          spawn: ExodusItemTaxMergeStack
          maxCount: 50

        - type: entity
          id: ExodusItemTaxMergeStack
          components:
          - type: Item
          - type: Stack
            stackType: ExodusItemTaxMergeUnits
            count: 5
          - type: StackPrice
            price: 100

        - type: entity
          id: ExodusItemTaxMergeReceiver
          parent: ExodusItemTaxMergeStack
          components:
          - type: Stack
            count: 1
          - type: ItemTax
            taxAccounts:
              Medical: 0.8
        """;

    [Test]
    public async Task DiamondKeepsTheHokkaidoCatalogMultiplierWithoutUnrelatedMedicalTax()
    {
        await RunTest((purchases, _) =>
        {
            Assert.That(purchases.TryQuotePrototypeBuy("MaterialDiamond1", 1, 2000, 2,
                out var quote, stackUnits: true), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(quote!.NominalPrice, Is.EqualTo(4000));
                Assert.That(quote.TotalPrice, Is.EqualTo(4000),
                    "The 1.5 pallet sale bound and 5% margin must not add another item's medical subsidy.");
                Assert.That(quote.Adjustment, Is.Zero);
            });
        });
    }

    [TestCase(UntaxedItem, 158)]
    [TestCase(MedicalItem, 284)]
    [TestCase("ExodusItemTaxMixedAccounts", 284)]
    public async Task OnlyThePurchasedItemsPositiveSupportedTaxesRaiseItsFloor(string prototype, int expected)
    {
        await RunTest((purchases, _) =>
        {
            var quote = Quote(purchases, prototype);
            // Untaxed: ceil(100 * 1.5 * 1.05). Medical: ceil(100 * 1.8 * 1.5 * 1.05).
            Assert.That(quote.TotalPrice, Is.EqualTo(expected));
        });
    }

    [Test]
    public async Task AnUnrelatedLiveTaxEditDoesNotChangeThePurchasePrice()
    {
        await RunTest((purchases, entities) =>
        {
            var before = Quote(purchases, UntaxedItem).TotalPrice;
            var unrelated = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                var tax = entities.AddComponent<ItemTaxComponent>(unrelated);
                tax.TaxAccounts[SectorBankAccount.Medical] = 100;
                Assert.That(Quote(purchases, UntaxedItem).TotalPrice, Is.EqualTo(before));
                tax.TaxAccounts[SectorBankAccount.Medical] = 200;
                Assert.That(Quote(purchases, UntaxedItem).TotalPrice, Is.EqualTo(before));
            }
            finally
            {
                entities.DeleteEntity(unrelated);
            }
        });
    }

    [Test]
    public async Task AContainerAndItsContentsKeepTheirOwnTaxRates()
    {
        await RunTest((purchases, _) =>
        {
            var quote = Quote(purchases, "ExodusItemTaxContainer", basePrice: 301);
            // Shell 101 with 50%, untaxed contents 100, medical contents 100 with 80%.
            // ceil((151.5 + 100 + 180) * 1.5 * 1.05) = 680.
            Assert.That(quote.TotalPrice, Is.EqualTo(680));
        });
    }

    [TestCase("ExodusItemTaxUntaxedPackage", 158)]
    [TestCase("ExodusItemTaxMedicalPackage", 284)]
    public async Task AUsePackagePricesTheTaxOfItsPayload(string prototype, int expected)
    {
        await RunTest((purchases, _) =>
        {
            Assert.That(Quote(purchases, prototype).TotalPrice, Is.EqualTo(expected),
                "Opening or retaining the wrapper must not apply its tax rate to the delivered payload.");
        });
    }

    [Test]
    public async Task SplittingAnUntaxedStackMustCoverTheCanonicalSpawnsTax()
    {
        await RunTest((purchases, _) =>
        {
            var quote = Quote(purchases, "ExodusItemTaxSplitStack", basePrice: 500);
            // Five 100-credit units can become medical-taxed canonical entities after splitting.
            Assert.That(quote.TotalPrice, Is.EqualTo(1418));
        });
    }

    [Test]
    public async Task MergingIntoATaxedVariantMustRemainCoveredWhenTheCanonicalSpawnIsUntaxed()
    {
        await RunTest((purchases, _) =>
        {
            var quote = Quote(purchases, "ExodusItemTaxMergeStack", basePrice: 500);
            // A compatible receiver keeps its 80% medical payout for all five transferred units.
            Assert.That(quote.TotalPrice, Is.EqualTo(1418));
        });
    }

    [Test]
    public async Task NonCashPalletsRaiseOnlyTheTaxedItemsCreditFloor()
    {
        await RunTest((purchases, entities) =>
        {
            var console = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            try
            {
                var pallet = entities.AddComponent<CargoPalletConsoleComponent>(console);
#pragma warning disable RA0002 // Configure the payout currency of this test endpoint.
                pallet.CashType = "Doubloon";
#pragma warning restore RA0002
                var modifier = entities.AddComponent<MarketModifierComponent>(console);
                modifier.Buy = false;
                modifier.Mod = 100;

                Assert.That(Quote(purchases, UntaxedItem).TotalPrice, Is.EqualTo(158),
                    "The token payout is not money and an untaxed item creates no sector credits.");
                var taxed = Quote(purchases, MedicalItem);
                // 100 value * 100 token modifier * 0.8 tax * 1.05 margin; float fields can round up by one.
                Assert.That(taxed.TotalPrice, Is.InRange(8400, 8401));
            }
            finally
            {
                entities.DeleteEntity(console);
            }
        });
    }

    [TestCase(MedicalItem, MedicalItem, UntaxedItem, 100, 284, 174)]
    [TestCase("ExodusItemTaxMergeStack", "ExodusItemTaxMergeReceiver", "ExodusItemTaxMergeStack", 500, 1418, 867)]
    public async Task PrototypeReloadRefreshesAnItemsTaxAndItsCompatibleStackBound(
        string purchased, string changed, string parent, int basePrice, int before, int after)
    {
        await RunTest((purchases, _, prototypes) =>
        {
            Assert.That(Quote(purchases, purchased, basePrice).TotalPrice, Is.EqualTo(before));
            var modified = new Dictionary<Type, HashSet<string>>();
            prototypes.LoadString($"""
                - type: entity
                  id: {changed}
                  parent: {parent}
                  components:
                  - type: ItemTax
                    taxAccounts:
                      Medical: 0.1
                """, overwrite: true, changed: modified);
            prototypes.ReloadPrototypes(modified);

            // The first quote populated the cache. Reducing 80% to 10% must change the next charge.
            Assert.That(Quote(purchases, purchased, basePrice).TotalPrice, Is.EqualTo(after));
            Assert.That(Quote(purchases, UntaxedItem).TotalPrice, Is.EqualTo(158),
                "Reloading a taxed commodity must not change the floor of an unrelated item.");
        }, dirty: true);
    }

    private static MarketPurchaseQuote Quote(MarketPurchaseSystem purchases, string prototype, double basePrice = 100)
    {
        Assert.That(purchases.TryQuotePrototypeBuy(prototype, 1, basePrice, 1, out var quote), Is.True);
        return quote!;
    }

    private static Task RunTest(Action<MarketPurchaseSystem, IEntityManager> assertion)
    {
        return RunTest((purchases, entities, _) => assertion(purchases, entities));
    }

    private static async Task RunTest(
        Action<MarketPurchaseSystem, IEntityManager, IPrototypeManager> assertion, bool dirty = false)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = dirty });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var configuration = server.ResolveDependency<IConfigurationManager>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var margin = configuration.GetCVar(EXCVars.MarketPurchaseMargin);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, false);
            configuration.SetCVar(EXCVars.MarketPurchaseMargin, 0.05f);
            try
            {
                assertion(server.System<MarketPurchaseSystem>(), server.ResolveDependency<IEntityManager>(),
                    server.ResolveDependency<IPrototypeManager>());
            }
            finally
            {
                configuration.SetCVar(EXCVars.MarketPurchaseMargin, margin);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
            }
        });
        await pair.CleanReturnAsync();
    }
}
