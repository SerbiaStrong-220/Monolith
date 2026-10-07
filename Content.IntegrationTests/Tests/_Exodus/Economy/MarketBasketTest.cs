// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.Linq;
using Content.Server._Exodus.Economy;
using Content.Server.Cargo.Systems;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketBasketTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusBasketLeaf
  components:
  - type: Item
    size: Tiny
  - type: StaticPrice
    price: 100

- type: entity
  id: ExodusBasketArmor
  parent: ExodusBasketLeaf
  components:
  - type: Armor
    modifiers:
      coefficients:
        Blunt: 0.5
  - type: StaticPrice
    price: 10

- type: entity
  id: ExodusBasketBattery
  parent: ExodusBasketLeaf
  components:
  - type: Battery
    maxCharge: 100
    startingCharge: 100
    pricePerJoule: 0.5
  - type: StaticPrice
    price: 10

- type: entity
  id: ExodusBasketMagazine
  parent: ExodusBasketLeaf
  components:
  - type: BallisticAmmoProvider
    proto: ExodusBasketLeaf
    capacity: 3
  - type: StaticPrice
    price: 10

- type: entity
  id: ExodusBasketInfiniteMagazine
  parent: ExodusBasketMagazine
  components:
  - type: BallisticAmmoProvider
    infiniteUnspawned: true

- type: entity
  id: ExodusBasketRevolver
  parent: ExodusBasketLeaf
  components:
  - type: RevolverAmmoProvider
    proto: ExodusBasketLeaf
    capacity: 3
  - type: StaticPrice
    price: 10

- type: entity
  id: ExodusBasketBox
  parent: BoxCardboard
  components:
  - type: StaticPrice
    price: 10
  - type: StorageFill
    contents:
    - id: ExodusBasketLeaf
      amount: 2

- type: entity
  id: ExodusBasketCrate
  parent: CrateGenericSteel
  components:
  - type: StaticPrice
    price: 50
  - type: StorageFill
    contents:
    - id: ExodusBasketBox
    - id: ExodusBasketArmor

- type: entity
  id: ExodusBasketContainer
  parent: ExodusBasketLeaf
  components:
  - type: ContainerContainer
    containers:
      contents: !type:Container
  - type: ContainerFill
    containers:
      contents:
      - ExodusBasketBattery

- type: entity
  id: ExodusBasketSlot
  parent: ExodusBasketLeaf
  components:
  - type: ItemSlots
    slots:
      payload:
        startingItem: ExodusBasketBattery

- type: entity
  id: ExodusBasketRandom
  parent: ExodusBasketBox
  components:
  - type: StorageFill
    contents:
    - id: ExodusBasketLeaf
      prob: 0.01
      amount: 2
      maxAmount: 4

- type: entity
  id: ExodusBasketCycle
  parent: ExodusBasketLeaf
  components:
  - type: ContainerFill
    containers:
      contents:
      - ExodusBasketCycle

- type: entity
  id: ExodusBasketGas
  parent: ExodusBasketLeaf
  components:
  - type: GasTank
    air:
      volume: 10
      temperature: 293.15
      moles:
      - 10

- type: entity
  id: ExodusBasketStack
  parent: ExodusBasketLeaf
  components:
  - type: Stack
    stackType: ExodusBasketStack
    count: 10
  - type: StackPrice
    price: 7

- type: stack
  id: ExodusBasketStack
  name: stack-steel
  spawn: ExodusBasketStack
  maxCount: 50

- type: entity
  id: ExodusBasketCompositeStack
  parent: ExodusBasketStack
  components:
  - type: ItemSlots
    slots:
      payload:
        startingItem: ExodusBasketBattery

- type: entity
  id: ExodusBasketPackageStack
  parent: ExodusBasketStack
  components:
  - type: SpawnItemsOnUse
    items:
    - id: ExodusBasketLeaf

- type: entity
  id: ExodusBasketBin
  parent: ExodusBasketLeaf
  components:
  - type: Bin
    initialContents: [ExodusBasketBattery, ExodusBasketArmor]

- type: entity
  id: ExodusBasketMachineBoard
  parent: ExodusBasketLeaf
  components:
  - type: MachineBoard
    prototype: ExodusBasketMachine
    stackRequirements:
      ExodusBasketStack: 20
    tagRequirements:
      CableCoil:
        amount: 2
        defaultPrototype: ExodusBasketBattery

- type: entity
  id: ExodusBasketMachine
  parent: ExodusBasketLeaf
  components:
  - type: Machine
    board: ExodusBasketMachineBoard

- type: entity
  id: ExodusBasketComputerBoard
  parent: ExodusBasketLeaf
  components:
  - type: ComputerBoard
    prototype: ExodusBasketComputer

- type: entity
  id: ExodusBasketComputer
  parent: ExodusBasketLeaf
  components:
  - type: Computer
    board: ExodusBasketComputerBoard

- type: entity
  id: ExodusBasketBody
  parent: ExodusBasketLeaf
  components:
  - type: Body
    prototype: ExodusBasketAnatomy

- type: body
  id: ExodusBasketAnatomy
  name: basket test body
  root: torso
  slots:
    torso:
      part: ExodusBasketBodyPart
      organs:
        heart: ExodusBasketOrgan

- type: entity
  id: ExodusBasketBodyPart
  parent: ExodusBasketLeaf
  components:
  - type: BodyPart
    partType: Torso

- type: entity
  id: ExodusBasketOrgan
  parent: ExodusBasketLeaf
  components:
  - type: Organ

- type: entity
  id: ExodusBasketMob
  parent: ExodusBasketBody
  components:
  - type: MobState
  - type: MobPrice
    price: 80
    labGrownPenalty: 0.5

- type: entity
  id: ExodusBasketLabMob
  parent: ExodusBasketMob
  components:
  - type: LabGrown

- type: reagent
  id: ExodusBasketReagent
  parent: Water
  pricePerUnit: 5

- type: weightedRandomFillSolution
  id: ExodusBasketRandomSolutionFill
  fills:
  - quantity: 10
    weight: 1
    reagents: [ExodusBasketReagent]

- type: entity
  id: ExodusBasketRandomSolution
  parent: ExodusBasketLeaf
  components:
  - type: SolutionContainerManager
    solutions:
      chemicals:
        maxVol: 10
  - type: RandomFillSolution
    solution: chemicals
    weightedRandomId: ExodusBasketRandomSolutionFill
";

    [TestCase("ExodusBasketArmor", 110)]
    [TestCase("ExodusBasketBattery", 60)]
    [TestCase("ExodusBasketBox", 210)]
    [TestCase("ExodusBasketCrate", 370)]
    [TestCase("ExodusBasketContainer", 160)]
    [TestCase("ExodusBasketSlot", 160)]
    [TestCase("ExodusBasketMagazine", 310)]
    [TestCase("ExodusBasketInfiniteMagazine", 10)]
    [TestCase("ExodusBasketRevolver", 310, false)]
    [TestCase("ExodusBasketBin", 270)]
    [TestCase("ExodusBasketMachine", 460)]
    [TestCase("ExodusBasketComputer", 200)]
    [TestCase("ExodusBasketBody", 300)]
    [TestCase("ExodusBasketMob", 340)]
    [TestCase("ExodusBasketLabMob", 380)]
    public async Task PrototypeAndLiveBasketIncludeRuntimePricesAndContents(string prototype, double expected,
        bool compareLegacyPrice = true)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var baskets = server.System<MarketBasketSystem>();
        var pricing = server.System<PricingSystem>();

        await server.WaitAssertion(() =>
        {
            Assert.That(baskets.TryGetPrototypeBasket(prototype, out var estimated, out var failure), Is.True, failure);
            var uid = entities.SpawnEntity(prototype, map.MapCoords);
            Assert.That(baskets.TryGetEntityBasket(uid, out var actual, out failure), Is.True, failure);
            Assert.Multiple(() =>
            {
                Assert.That(estimated.Exact, Is.True);
                Assert.That(actual.Exact, Is.True);
                Assert.That(estimated.NominalValue, Is.EqualTo(expected).Within(0.00001));
                Assert.That(actual.NominalValue, Is.EqualTo(expected).Within(0.00001));
                if (compareLegacyPrice)
                    Assert.That(pricing.GetPrice(uid), Is.EqualTo(expected).Within(0.00001));
                Assert.That(actual.Lines.GroupBy(x => x.MarketKey).Select(x => (x.Key, Quantity: x.Sum(y => y.Quantity))),
                    Is.EquivalentTo(estimated.Lines.GroupBy(x => x.MarketKey)
                        .Select(x => (x.Key, Quantity: x.Sum(y => y.Quantity)))));
            });
            entities.DeleteEntity(uid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GasAndStackUseCommodityUnitsAndSeparateShells()
    {
        await using var pair = await PoolManager.GetServerClient();
        var baskets = pair.Server.System<MarketBasketSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(baskets.TryGetPrototypeBasket("ExodusBasketGas", out var gas, out var failure), Is.True, failure);
            Assert.That(baskets.TryGetPrototypeBasket("ExodusBasketStack", out var stack, out failure), Is.True, failure);
            Assert.Multiple(() =>
            {
                Assert.That(gas.Lines.Single(x => x.MarketKey == "proto:ExodusBasketGas").UnitBasePrice, Is.EqualTo(100));
                Assert.That(gas.Lines.Single(x => x.MarketKey == "gas:Oxygen").Quantity, Is.EqualTo(10));
                Assert.That(stack.SpawnedUnits, Is.EqualTo(10));
                Assert.That(stack.Lines.Single().MarketKey, Is.EqualTo("stack:ExodusBasketStack"));
                Assert.That(stack.Lines.Single().Quantity, Is.EqualTo(10));
                Assert.That(stack.NominalValue, Is.EqualTo(70));
                Assert.That(stack.Lines.Single().ResaleUnitPrice, Is.EqualTo(7).Within(0.00001));
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RandomContentsAreUpperBoundsAndCannotPretendToBeExactDeliveries()
    {
        await using var pair = await PoolManager.GetServerClient();
        var baskets = pair.Server.System<MarketBasketSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(baskets.TryGetPrototypeBasket("ExodusBasketRandom", out var basket, out var failure), Is.True, failure);
            Assert.Multiple(() =>
            {
                Assert.That(basket.Exact, Is.False);
                Assert.That(basket.NominalValue, Is.GreaterThanOrEqualTo(410));
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RandomSolutionInitialValueIsIncludedInTheBound()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.ResolveDependency<IEntityManager>();
        var baskets = pair.Server.System<MarketBasketSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(baskets.TryGetPrototypeBasket("ExodusBasketRandomSolution", out var basket, out var failure),
                Is.True, failure);
            var uid = entities.SpawnEntity("ExodusBasketRandomSolution", map.MapCoords);
            Assert.That(baskets.TryGetEntityBasket(uid, out var actual, out failure), Is.True, failure);
            Assert.Multiple(() =>
            {
                Assert.That(basket.Exact, Is.False);
                Assert.That(basket.NominalValue, Is.EqualTo(150));
                Assert.That(actual.NominalValue, Is.EqualTo(150));
            });
            entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CyclicContentsFailClosedAndRepeatedQuotesReuseTheCache()
    {
        await using var pair = await PoolManager.GetServerClient();
        var baskets = pair.Server.System<MarketBasketSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(baskets.TryGetPrototypeBasket("ExodusBasketCycle", out _, out var failure), Is.False);
            Assert.That(failure, Does.Contain("Cyclic"));
            Assert.That(baskets.TryGetPrototypeBasket("ExodusBasketBox", out var first, out failure), Is.True, failure);
            Assert.That(baskets.TryGetPrototypeBasket("ExodusBasketBox", out var second, out failure), Is.True, failure);
            Assert.That(second, Is.SameAs(first));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ExodusBasketCompositeStack")]
    [TestCase("ExodusBasketPackageStack")]
    public async Task CompositeStacksRequireASplitContentsAdapter(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var baskets = pair.Server.System<MarketBasketSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(baskets.TryGetPrototypeBasket(prototype, out _, out var failure), Is.False);
            Assert.That(failure, Does.Contain("Composite"));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReportCatalogBasketCoverageWithoutSpawningProducts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
        var baskets = pair.Server.System<MarketBasketSystem>();
        await pair.Server.WaitAssertion(() =>
        {
            var cargo = prototypes.EnumeratePrototypes<CargoProductPrototype>()
                .Where(x => !x.Abstract).Select(x => x.Product).Distinct().ToArray();
            var vending = new HashSet<EntProtoId>();
            foreach (var inventory in prototypes.EnumeratePrototypes<VendingMachineInventoryPrototype>())
            {
                foreach (var id in inventory.StartingInventory.Keys)
                    vending.Add(id);
                if (inventory.EmaggedInventory != null)
                {
                    foreach (var id in inventory.EmaggedInventory.Keys)
                        vending.Add(id);
                }
                if (inventory.ContrabandInventory != null)
                {
                    foreach (var id in inventory.ContrabandInventory.Keys)
                        vending.Add(id);
                }
            }

            Report("Cargo", cargo);
            Report("Vending", vending);

            void Report(string channel, IEnumerable<EntProtoId> products)
            {
                var exact = 0;
                var bounded = 0;
                var failed = 0;
                foreach (var id in products.OrderBy(x => x.Id))
                {
                    if (!baskets.TryGetPrototypeBasket(id, out var basket, out _))
                    {
                        failed++;
                    }
                    else if (!basket.Exact)
                    {
                        bounded++;
                    }
                    else
                    {
                        exact++;
                    }
                }
                TestContext.Progress.WriteLine($"Basket coverage {channel}: exact={exact}, bounded={bounded}, blocked={failed}");
                Assert.That(exact, Is.GreaterThan(0), $"{channel} catalog has no exact delivery baskets.");
            }
        });
        await pair.CleanReturnAsync();
    }
}
