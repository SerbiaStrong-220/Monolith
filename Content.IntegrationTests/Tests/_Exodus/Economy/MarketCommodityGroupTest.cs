// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Content.Server._Exodus.Economy;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy;
using Content.Shared.Atmos;
using Content.Shared.Stacks;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketCommodityGroupTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: Tag
          id: ExodusCommodityTestTag

        - type: entity
          id: ExodusCommodityTestPlain
          components:
          - type: Item
          - type: StaticPrice
            price: 100

        - type: entity
          id: ExodusCommodityTestExact
          parent: ExodusCommodityTestPlain

        - type: entity
          id: ExodusCommodityTestExactChild
          parent: ExodusCommodityTestExact

        - type: entity
          id: ExodusCommodityTestAncestor
          abstract: true
          parent: ExodusCommodityTestPlain

        - type: entity
          id: ExodusCommodityTestIntermediate
          abstract: true
          parent: ExodusCommodityTestAncestor

        - type: entity
          id: ExodusCommodityTestDescendant
          parent: ExodusCommodityTestIntermediate

        - type: entity
          id: ExodusCommodityTestParentSelf
          parent: ExodusCommodityTestPlain

        - type: entity
          id: ExodusCommodityTestComponent
          parent: ExodusCommodityTestPlain
          components:
          - type: MarketCommodityTestMarker

        - type: entity
          id: ExodusCommodityTestTagOnly
          parent: ExodusCommodityTestPlain
          components:
          - type: Tag
            tags: [ExodusCommodityTestTag]

        - type: entity
          id: ExodusCommodityTestExcluded
          parent: ExodusCommodityTestComponent
          components:
          - type: MarketCommodityTestExcluded

        - type: entity
          id: ExodusCommodityTestPriority
          parent: ExodusCommodityTestComponent

        - type: entity
          id: ExodusCommodityTestTie
          parent: ExodusCommodityTestPlain

        - type: stack
          id: ExodusCommodityTestUnits
          name: stack-steel
          spawn: ExodusCommodityTestCanonical
          maxCount: 50

        - type: entity
          id: ExodusCommodityTestCanonical
          parent: ExodusCommodityTestPlain
          components:
          - type: Stack
            stackType: ExodusCommodityTestUnits
            count: 1

        - type: entity
          id: ExodusCommodityTestVariant
          parent: ExodusCommodityTestCanonical
          components:
          - type: Stack
            count: 20

        - type: entity
          id: ExodusCommodityTestTank
          parent: ExodusCommodityTestPlain
          components:
          - type: GasTank
            air:
              volume: 10
              temperature: 293.15
              moles:
              - 10

        - type: marketCommodityRule
          id: ExodusCommodityTestSelectors
          group: HighPrecision
          priority: 100000
          prototypes: [ExodusCommodityTestExact, ExodusCommodityTestTank, ExodusCommodityTestExcluded]
          parents: [ExodusCommodityTestAncestor, ExodusCommodityTestParentSelf]
          components: [MarketCommodityTestMarker]
          excludeComponents: [MarketCommodityTestExcluded]
          tags: [ExodusCommodityTestTag]
          stackTypes: [ExodusCommodityTestUnits]

        - type: marketCommodityRule
          id: ExodusCommodityTestHigherPriority
          group: UltraPrecision
          priority: 100001
          prototypes: [ExodusCommodityTestPriority, ExodusCommodityTestVariant]

        - type: marketCommodityRule
          id: ExodusCommodityTestZTie
          group: UltraPrecision
          priority: 100002
          prototypes: [ExodusCommodityTestTie]

        - type: marketCommodityRule
          id: ExodusCommodityTestATie
          group: RawMaterials
          priority: 100002
          prototypes: [ExodusCommodityTestTie]
        """;

    [TestCase("ExodusCommodityTestPlain", "General", null)]
    [TestCase("ExodusCommodityTestExact", "HighPrecision", "ExodusCommodityTestSelectors")]
    [TestCase("ExodusCommodityTestExactChild", "General", null)]
    [TestCase("ExodusCommodityTestDescendant", "HighPrecision", "ExodusCommodityTestSelectors")]
    [TestCase("ExodusCommodityTestParentSelf", "HighPrecision", "ExodusCommodityTestSelectors")]
    [TestCase("ExodusCommodityTestComponent", "HighPrecision", "ExodusCommodityTestSelectors")]
    [TestCase("ExodusCommodityTestTagOnly", "HighPrecision", "ExodusCommodityTestSelectors")]
    [TestCase("ExodusCommodityTestExcluded", "General", null)]
    [TestCase("ExodusCommodityTestPriority", "UltraPrecision", "ExodusCommodityTestHigherPriority")]
    [TestCase("ExodusCommodityTestTie", "RawMaterials", "ExodusCommodityTestATie")]
    public async Task SelectorsExclusionsAndRuleOrderChooseOneClassification(
        string prototype, string expectedGroup, string expectedRule)
    {
        await RunTest((groups, _, _) =>
        {
            Assert.That(groups.TryGetClassification($"proto:{prototype}", out var classification), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(classification.Group.Id, Is.EqualTo(expectedGroup));
                Assert.That(classification.Rule?.Id, Is.EqualTo(expectedRule));
                Assert.That(groups.GetPrototypeGroup(prototype).Id, Is.EqualTo(expectedGroup));
            });
        });
    }

    [TestCase("ExodusCommodityTestCanonical")]
    [TestCase("ExodusCommodityTestVariant")]
    public async Task StackVariantsKeepTheCanonicalCommodityDespiteAHigherPriorityVariantRule(string prototype)
    {
        await RunTest((groups, _, _) =>
        {
            Assert.That(groups.TryGetClassification($"proto:{prototype}", out var classification), Is.True);
            Assert.That(groups.TryGetClassification("stack:ExodusCommodityTestUnits", out var stack), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(classification.Group.Id, Is.EqualTo("HighPrecision"));
                Assert.That(classification.Rule?.Id, Is.EqualTo("ExodusCommodityTestSelectors"));
                Assert.That(stack.Group.Id, Is.EqualTo("HighPrecision"));
                Assert.That(stack.Rule?.Id, Is.EqualTo("ExodusCommodityTestSelectors"));
                Assert.That(groups.GetPrototypeGroup(prototype).Id, Is.EqualTo("HighPrecision"));
            });
        });
    }

    [TestCase("MaterialAmericium", "UltraPrecision")]
    [TestCase("MaterialAmericium1", "UltraPrecision")]
    [TestCase("MaterialAmericium10", "UltraPrecision")]
    [TestCase("MobShipRepairDrone", "UltraPrecision")]
    [TestCase("MobShipRepairDroneFleetTSF", "UltraPrecision")]
    [TestCase("MobShipRepairDroneFleetPDV", "UltraPrecision")]
    [TestCase("MobShipRepairDroneAsakim", "UltraPrecision")]
    [TestCase("MachineFtlSuppressorTsf", "UltraPrecision")]
    [TestCase("BulkMiningRefinery", "UltraPrecision")]
    [TestCase("ClothingOuterHardsuitAsakim", "UltraPrecision")]
    [TestCase("ClothingOuterHardsuitAsakimUnremoveable", "UltraPrecision")]
    [TestCase("ClothingHelmetHardsuitAsakim", "UltraPrecision")]
    [TestCase("ClothingOuterHardsuitCenturionAsakim", "UltraPrecision")]
    [TestCase("ClothingOuterHardsuitCenturionAsakimUnremoveable", "UltraPrecision")]
    [TestCase("ClothingHelmetHardsuitCenturionAsakim", "UltraPrecision")]
    [TestCase("ClothingOuterHardsuitPrefectAsakim", "UltraPrecision")]
    [TestCase("ClothingOuterHardsuitPrefectAsakimUnremoveable", "UltraPrecision")]
    [TestCase("ClothingHelmetHardsuitPrefectAsakim", "UltraPrecision")]
    [TestCase("AdvancedCapacitorStockPart", "HighPrecision")]
    [TestCase("NanoManipulatorStockPart", "HighPrecision")]
    [TestCase("AdvancedMatterBinStockPart", "HighPrecision")]
    [TestCase("SuperCapacitorStockPart", "HighPrecision")]
    [TestCase("PicoManipulatorStockPart", "HighPrecision")]
    [TestCase("SuperMatterBinStockPart", "HighPrecision")]
    [TestCase("QuadraticCapacitorStockPart", "HighPrecision")]
    [TestCase("FemtoManipulatorStockPart", "HighPrecision")]
    [TestCase("BluespaceMatterBinStockPart", "HighPrecision")]
    [TestCase("ResearchDisk", "HighPrecision")]
    [TestCase("TechDiskHmeAdvancedOrgans", "HighPrecision")]
    [TestCase("MicroprocessorEconomy2", "HighPrecision")]
    [TestCase("SiliconWaferEconomy", "HighPrecision")]
    [TestCase("MaterialIndustryElectronicsAdvanced", "HighPrecision")]
    [TestCase("MedicatedSuture", "HighPrecision")]
    [TestCase("HmeSynthHeart", "HighPrecision")]
    [TestCase("BulkMiningPipeStack1", "HighPrecision")]
    [TestCase("BulkMiningPipeStack10", "HighPrecision")]
    [TestCase("BulkMiningRefineryCircuitboard", "HighPrecision")]
    [TestCase("GasMiningDrill", "General")]
    [TestCase("GasMiningDrillFlatpack", "General")]
    [TestCase("MicroprocessorEconomy1", "General")]
    [TestCase("BlankMediPen", "General")]
    [TestCase("AloeCream", "General")]
    [TestCase("BioSynthEyes", "General")]
    [TestCase("BioSynthHeart", "General")]
    [TestCase("HmeSynthEyes", "General")]
    [TestCase("HiveSynthEyes", "General")]
    [TestCase("APCElectronics", "General")]
    [TestCase("MaterialDiamond", "RawMaterials")]
    [TestCase("MaterialDiamond1", "RawMaterials")]
    [TestCase("SheetSteel", "RawMaterials")]
    [TestCase("SheetSteel10", "RawMaterials")]
    [TestCase("SheetSteel1", "RawMaterials")]
    [TestCase("MaterialBluespace1", "RawMaterials")]
    [TestCase("BluespaceOre1", "RawMaterials")]
    public async Task RealProductionFamiliesKeepTheirAgreedGroupsAcrossMarketKeys(string prototype, string expectedGroup)
    {
        await RunTest((groups, entities, prototypes) =>
        {
            var entityPrototype = prototypes.Index<EntityPrototype>(prototype);
            Assert.That(groups.TryGetClassification($"proto:{prototype}", out var classification), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(classification.Group.Id, Is.EqualTo(expectedGroup));
                Assert.That(groups.GetPrototypeGroup(prototype).Id, Is.EqualTo(expectedGroup));
                if (entityPrototype.TryGetComponent<StackComponent>(out var stack, entities.ComponentFactory))
                    Assert.That(groups.GetGroup($"stack:{stack.StackTypeId}").Id, Is.EqualTo(expectedGroup));
            });
        });
    }

    [Test]
    public async Task AProductBasketKeepsTheGasSeparateFromTheContainersGroup()
    {
        await RunTest((groups, entities, _) =>
        {
            var baskets = entities.System<MarketBasketSystem>();
            Assert.That(baskets.TryGetPrototypeBasket("ExodusCommodityTestTank", out var basket, out var failure),
                Is.True, failure);
            var lines = basket.Lines.ToDictionary(line => line.MarketKey);
            Assert.That(lines.Keys, Is.EquivalentTo(new[] { "proto:ExodusCommodityTestTank", "gas:Oxygen" }));
            Assert.Multiple(() =>
            {
                Assert.That(groups.GetGroup("proto:ExodusCommodityTestTank").Id, Is.EqualTo("HighPrecision"));
                Assert.That(groups.GetPrototypeGroup("ExodusCommodityTestTank").Id, Is.EqualTo("HighPrecision"));
                Assert.That(groups.GetGroup("gas:Oxygen").Id, Is.EqualTo("Gases"));
                Assert.That(lines["proto:ExodusCommodityTestTank"].UnitBasePrice, Is.EqualTo(100));
                Assert.That(lines["gas:Oxygen"].Quantity, Is.EqualTo(10));
            });
        });
    }

    [Test]
    public async Task ReloadingARuleReplacesTheCachedClassification()
    {
        await RunTest((groups, _, prototypes) =>
        {
            Assert.That(groups.GetPrototypeGroup("ExodusCommodityTestPriority").Id, Is.EqualTo("UltraPrecision"));
            try
            {
                ReloadGroup("RawMaterials");
                Assert.That(groups.TryGetClassification("proto:ExodusCommodityTestPriority", out var classification), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(classification.Group.Id, Is.EqualTo("RawMaterials"));
                    Assert.That(groups.GetPrototypeGroup("ExodusCommodityTestPriority").Id, Is.EqualTo("RawMaterials"));
                    Assert.That(groups.GetPrototypeGroup("ExodusCommodityTestExact").Id, Is.EqualTo("HighPrecision"));
                });
            }
            finally
            {
                ReloadGroup("UltraPrecision");
            }

            void ReloadGroup(string group)
            {
                var modified = new Dictionary<Type, HashSet<string>>();
                prototypes.LoadString($"""
                    - type: marketCommodityRule
                      id: ExodusCommodityTestHigherPriority
                      group: {group}
                      priority: 100001
                      prototypes: [ExodusCommodityTestPriority, ExodusCommodityTestVariant]
                    """, overwrite: true, changed: modified);
                prototypes.ReloadPrototypes(modified);
            }
        });
    }

    [Test]
    public async Task TradingOneCommodityDoesNotMoveAnotherIndexInTheSameGroup()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var configuration = server.ResolveDependency<IConfigurationManager>();
            var market = server.System<DynamicMarketSystem>();
            var groups = server.System<MarketCommodityGroupSystem>();
            var enabled = configuration.GetCVar(EXCVars.DynamicMarketEnabled);
            var persist = configuration.GetCVar(EXCVars.DynamicMarketPersist);
            var minFactor = configuration.GetCVar(EXCVars.DynamicMarketMinFactor);
            var maxFactor = configuration.GetCVar(EXCVars.DynamicMarketMaxFactor);
            var buyImpact = configuration.GetCVar(EXCVars.DynamicMarketBuyImpact);
            var sellImpact = configuration.GetCVar(EXCVars.DynamicMarketSellImpact);
            var referenceVolume = configuration.GetCVar(EXCVars.DynamicMarketReferenceVolume);
            const string traded = "proto:ExodusCommodityTestExact";
            const string unrelated = "proto:ExodusCommodityTestTagOnly";
            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            configuration.SetCVar(EXCVars.DynamicMarketEnabled, true);
            configuration.SetCVar(EXCVars.DynamicMarketMinFactor, 0.1f);
            configuration.SetCVar(EXCVars.DynamicMarketMaxFactor, 10f);
            configuration.SetCVar(EXCVars.DynamicMarketBuyImpact, 1f);
            configuration.SetCVar(EXCVars.DynamicMarketSellImpact, 1f);
            configuration.SetCVar(EXCVars.DynamicMarketReferenceVolume, 1f);
            market.SetFactor(traded, 1);
            market.SetFactor(unrelated, 1);
            try
            {
                Assert.That(groups.GetGroup(traded).Id, Is.EqualTo("HighPrecision"));
                Assert.That(groups.GetGroup(unrelated).Id, Is.EqualTo("HighPrecision"));
                var cost = market.CalculateSequentialBuyCost(traded, 100, 1, 1, 1, null, true);
                Assert.Multiple(() =>
                {
                    Assert.That(cost, Is.EqualTo(100 * (Math.E - 1)).Within(0.000001));
                    Assert.That(market.GetFactor(traded), Is.EqualTo(Math.E).Within(0.000001));
                    Assert.That(market.GetFactor(unrelated), Is.EqualTo(1));
                    Assert.That(market.CalculateSequentialBuyCost(unrelated, 100, 1, 1, 1, null, false),
                        Is.EqualTo(100 * (Math.E - 1)).Within(0.000001));
                });
            }
            finally
            {
                market.ResetKey(traded);
                market.ResetKey(unrelated);
                configuration.SetCVar(EXCVars.DynamicMarketBuyImpact, buyImpact);
                configuration.SetCVar(EXCVars.DynamicMarketSellImpact, sellImpact);
                configuration.SetCVar(EXCVars.DynamicMarketReferenceVolume, referenceVolume);
                configuration.SetCVar(EXCVars.DynamicMarketMinFactor, minFactor);
                configuration.SetCVar(EXCVars.DynamicMarketMaxFactor, maxFactor);
                configuration.SetCVar(EXCVars.DynamicMarketEnabled, enabled);
                configuration.SetCVar(EXCVars.DynamicMarketPersist, persist);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryLoadedEntityStackAndGasHasAValidGroupAndExportsItsClassification()
    {
        var csv = new StringBuilder("kind,id,name,marketKey,group,rule\n");
        await RunTest((groups, entities, prototypes) =>
        {
            ValidateRuleReferences(prototypes, entities.ComponentFactory);
            var entityClassifications = groups.GetPrototypeClassifications();
            var keyClassifications = groups.GetClassifications();
            var count = 0;
            foreach (var prototype in prototypes.EnumeratePrototypes<EntityPrototype>().OrderBy(p => p.ID, StringComparer.Ordinal))
            {
                if (prototype.Abstract)
                    continue;

                Assert.That(entityClassifications.TryGetValue(prototype.ID, out var classification), Is.True, prototype.ID);
                var key = prototype.TryGetComponent<StackComponent>(out var stack, entities.ComponentFactory)
                    ? $"stack:{stack.StackTypeId}"
                    : $"proto:{prototype.ID}";
                AddRow("entity", prototype.ID, prototype.Name, key, classification);
                count++;
            }
            Assert.That(count, Is.GreaterThan(1000), "The export must include the real loaded content, not just fixtures.");

            foreach (var stack in prototypes.EnumeratePrototypes<StackPrototype>().OrderBy(p => p.ID, StringComparer.Ordinal))
            {
                var key = $"stack:{stack.ID}";
                Assert.That(keyClassifications.TryGetValue(key, out var classification), Is.True, key);
                AddRow("stack", stack.ID, stack.Name, key, classification);
            }

            for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
            {
                var id = ((Gas) i).ToString();
                var key = $"gas:{id}";
                Assert.That(keyClassifications.TryGetValue(key, out var classification), Is.True, key);
                Assert.That(classification.Group.Id, Is.EqualTo("Gases"), key);
                AddRow("gas", id, id, key, classification);
            }

            void AddRow(string kind, string id, string name, string key, MarketCommodityClassification classification)
            {
                Assert.That(prototypes.TryIndex(classification.Group, out _), Is.True, key);
                if (classification.Rule is { } rule)
                    Assert.That(prototypes.TryIndex(rule, out _), Is.True, key);
                csv.AppendLine(string.Join(",", new[] { kind, id, name, key, classification.Group.Id, classification.Rule?.Id ?? "" }
                    .Select(value => $"\"{value.Replace("\"", "\"\"")}\"")));
            }
        });

        var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "market-commodity-groups.csv");
        await File.WriteAllTextAsync(path, csv.ToString(), new UTF8Encoding(false));
        TestContext.AddTestAttachment(path, "Classification of every loaded entity prototype, stack type and gas.");
    }

    private static void ValidateRuleReferences(IPrototypeManager prototypes, IComponentFactory factory)
    {
        Assert.Multiple(() =>
        {
            foreach (var rule in prototypes.EnumeratePrototypes<MarketCommodityRulePrototype>())
            {
                Assert.That(prototypes.TryIndex(rule.Group, out _), Is.True, $"{rule.ID}: group {rule.Group}");
                foreach (var prototype in rule.Prototypes)
                    Assert.That(prototypes.TryIndex(prototype, out _), Is.True, $"{rule.ID}: prototype {prototype}");
                foreach (var parent in rule.Parents)
                    Assert.That(prototypes.HasMapping<EntityPrototype>(parent.Id), Is.True, $"{rule.ID}: parent {parent}");
                foreach (var tag in rule.Tags)
                    Assert.That(prototypes.TryIndex(tag, out _), Is.True, $"{rule.ID}: tag {tag}");
                foreach (var stack in rule.StackTypes)
                    Assert.That(prototypes.TryIndex(stack, out _), Is.True, $"{rule.ID}: stack {stack}");
                foreach (var component in rule.Components)
                    Assert.That(factory.TryGetRegistration(component, out _), Is.True, $"{rule.ID}: component {component}");
                foreach (var component in rule.ExcludeComponents)
                    Assert.That(factory.TryGetRegistration(component, out _), Is.True, $"{rule.ID}: excluded component {component}");
            }
        });
    }

    private static async Task RunTest(
        Action<MarketCommodityGroupSystem, IEntityManager, IPrototypeManager> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitAssertion(() => assertion(server.System<MarketCommodityGroupSystem>(), server.EntMan,
            server.ResolveDependency<IPrototypeManager>()));
        await pair.CleanReturnAsync();
    }
}

[RegisterComponent]
public sealed partial class MarketCommodityTestMarkerComponent : Component;

[RegisterComponent]
public sealed partial class MarketCommodityTestExcludedComponent : Component;
