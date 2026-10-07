// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Systems;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class DefaultItemPriceTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: material
          id: ExodusDefaultPriceTestMaterial
          icon:
            sprite: Objects/Materials/Sheets/metal.rsi
            state: steel
          price: 10

        - type: reagent
          id: ExodusDefaultPriceTestReagent
          parent: Water
          pricePerUnit: 4

        - type: entity
          id: ExodusDefaultPriceTestComposition
          components:
          - type: Item
            size: Tiny
          - type: PhysicalComposition
            materialComposition:
              ExodusDefaultPriceTestMaterial: 3
            chemicalComposition:
              ExodusDefaultPriceTestReagent: 5

        - type: entity
          id: ExodusDefaultPriceTestMaterialOnly
          components:
          - type: Item
            size: Tiny
          - type: PhysicalComposition
            materialComposition:
              ExodusDefaultPriceTestMaterial: 3

        - type: entity
          id: ExodusDefaultPriceTestChemicalOnly
          components:
          - type: Item
            size: Tiny
          - type: PhysicalComposition
            chemicalComposition:
              ExodusDefaultPriceTestReagent: 5

        - type: entity
          id: ExodusDefaultPriceTestExplicitZero
          parent: ExodusDefaultPriceTestComposition
          components:
          - type: StaticPrice
            price: 0

        - type: entity
          id: ExodusDefaultPriceTestPriced
          parent: ExodusDefaultPriceTestComposition
          components:
          - type: StaticPrice
            price: 7

        - type: entity
          id: ExodusDefaultPriceTestStack
          parent: ExodusDefaultPriceTestComposition
          components:
          - type: Stack
            stackType: ExodusDefaultPriceTestUnits
            count: 10
            lingering: true

        - type: stack
          id: ExodusDefaultPriceTestUnits
          name: stack-steel
          spawn: ExodusDefaultPriceTestStack
          maxCount: 50

        - type: entity
          id: ExodusDefaultPriceTestCheapComposition
          components:
          - type: Item
            size: Tiny
          - type: PhysicalComposition
            chemicalComposition:
              ExodusDefaultPriceTestReagent: 0.125

        - type: entity
          id: ExodusDefaultPriceTestLatheProduct
          parent: ExodusDefaultPriceTestComposition

        - type: latheRecipe
          id: ExodusDefaultPriceTestBatchRecipe
          result: ExodusDefaultPriceTestLatheProduct
          resultCount: 4
          materials:
            ExodusDefaultPriceTestMaterial: 100

        - type: entity
          id: ExodusDefaultPriceTestMachinePart
          parent: ExodusDefaultPriceTestComposition
          components:
          - type: MachinePart
            part: Capacitor
            rating: 2

        - type: entity
          id: ExodusDefaultPriceTestCraftedMachinePart
          parent: ExodusDefaultPriceTestMachinePart

        - type: latheRecipe
          id: ExodusDefaultPriceTestMachinePartRecipe
          result: ExodusDefaultPriceTestCraftedMachinePart
          materials:
            ExodusDefaultPriceTestMaterial: 10
        """;

    [TestCase("ExodusDefaultPriceTestMaterialOnly", 30.0)]
    [TestCase("ExodusDefaultPriceTestChemicalOnly", 20.0)]
    [TestCase("ExodusDefaultPriceTestComposition", 50.0)]
    [TestCase("ExodusDefaultPriceTestExplicitZero", 50.0)]
    [TestCase("ExodusDefaultPriceTestCheapComposition", 1.0)]
    [TestCase("ExodusDefaultPriceTestLatheProduct", 162.5)]
    [TestCase("ExodusDefaultPriceTestMachinePart", 600.0)]
    [TestCase("ExodusDefaultPriceTestCraftedMachinePart", 65.0)]
    public async Task UnpricedItemsUseCompositionValueWithoutReducingExistingFallback(string prototypeId, double expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var pricing = server.System<PricingSystem>();
        var fallback = server.System<DefaultItemPriceSystem>();

        await server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity(prototypeId, MapCoordinates.Nullspace);
            var prototype = entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!;

            Assert.Multiple(() =>
            {
                Assert.That(pricing.GetPrice(uid), Is.EqualTo(expected));
                Assert.That(pricing.GetEstimatedPrice(prototype), Is.EqualTo(expected));
                Assert.That(fallback.ApplyFallback(uid, 0), Is.EqualTo(expected));
                Assert.That(fallback.ApplyFallback(prototype, 0), Is.EqualTo(expected));
                Assert.That(pricing.GetEstimatedPrice(prototype, out _, applyFallback: false), Is.Zero);
            });

            entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(-0.01)]
    [TestCase(0.01)]
    public async Task EffectivelyZeroPricesUseCompositionFallback(double currentPrice)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var fallback = server.System<DefaultItemPriceSystem>();

        await server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity("ExodusDefaultPriceTestComposition", MapCoordinates.Nullspace);
            var prototype = entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!;

            Assert.Multiple(() =>
            {
                Assert.That(fallback.ApplyFallback(uid, currentPrice), Is.EqualTo(50));
                Assert.That(fallback.ApplyFallback(prototype, currentPrice), Is.EqualTo(50));
            });

            entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(-0.011)]
    [TestCase(0.011)]
    [TestCase(7.0)]
    public async Task ExistingPricesRemainUnchangedDespiteMoreValuableComposition(double currentPrice)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var pricing = server.System<PricingSystem>();
        var fallback = server.System<DefaultItemPriceSystem>();

        await server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity("ExodusDefaultPriceTestPriced", MapCoordinates.Nullspace);
            var prototype = entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!;

            Assert.Multiple(() =>
            {
                Assert.That(pricing.GetPrice(uid), Is.EqualTo(7));
                Assert.That(pricing.GetEstimatedPrice(prototype), Is.EqualTo(7));
                Assert.That(fallback.ApplyFallback(uid, currentPrice), Is.EqualTo(currentPrice));
                Assert.That(fallback.ApplyFallback(prototype, currentPrice), Is.EqualTo(currentPrice));
            });

            entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(0, 0.0)]
    [TestCase(1, 50.0)]
    [TestCase(3, 150.0)]
    [TestCase(10, 500.0)]
    public async Task CompositionFallbackUsesCurrentStackCount(int count, double expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var pricing = server.System<PricingSystem>();
        var fallback = server.System<DefaultItemPriceSystem>();
        var stacks = server.System<SharedStackSystem>();

        await server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity("ExodusDefaultPriceTestStack", MapCoordinates.Nullspace);
            var prototype = entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!;
            stacks.SetCount(uid, count);

            Assert.Multiple(() =>
            {
                Assert.That(entities.EntityExists(uid), Is.True);
                Assert.That(pricing.GetPrice(uid), Is.EqualTo(expected));
                Assert.That(fallback.ApplyFallback(uid, 0), Is.EqualTo(expected));
                Assert.That(pricing.GetEstimatedPrice(prototype), Is.EqualTo(500));
                Assert.That(fallback.ApplyFallback(prototype, 0), Is.EqualTo(500));
            });

            entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RuntimeCompositionChangesDoNotUsePrototypeAmounts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var pricing = server.System<PricingSystem>();
        var fallback = server.System<DefaultItemPriceSystem>();

        await server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity("ExodusDefaultPriceTestComposition", MapCoordinates.Nullspace);
            var prototype = entities.GetComponent<MetaDataComponent>(uid).EntityPrototype!;
            entities.GetComponent<PhysicalCompositionComponent>(uid).MaterialComposition["ExodusDefaultPriceTestMaterial"] = 1;

            Assert.Multiple(() =>
            {
                Assert.That(pricing.GetPrice(uid), Is.EqualTo(30));
                Assert.That(fallback.ApplyFallback(uid, 0), Is.EqualTo(30));
                Assert.That(pricing.GetEstimatedPrice(prototype), Is.EqualTo(50));
                Assert.That(fallback.ApplyFallback(prototype, 0), Is.EqualTo(50));
            });

            entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }
}
