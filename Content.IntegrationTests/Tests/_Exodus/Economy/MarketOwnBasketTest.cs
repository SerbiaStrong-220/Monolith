// (c) Space Exodus Team - EXDS-RL with CLA
using System.Linq;
using Content.Server._Exodus.Economy;
using Content.Server.Construction.Components;
using Content.Server.Stack;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Materials;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketOwnBasketTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusOwnBasketLeaf
  components:
  - type: StaticPrice
    price: 100

- type: entity
  id: ExodusOwnBasketWrapper
  components:
  - type: StaticPrice
    price: 10

- type: entity
  id: ExodusOwnBasketPackage
  parent: ExodusOwnBasketWrapper
  components:
  - type: SpawnItemsOnUse
    items:
    - id: ExodusOwnBasketLeaf
      amount: 2

- type: entity
  id: ExodusOwnBasketRandomPackage
  parent: ExodusOwnBasketWrapper
  components:
  - type: SpawnItemsOnUse
    items:
    - id: ExodusOwnBasketLeaf
      prob: 0.5
      amount: 1
      maxAmount: 4

- type: entity
  id: ExodusOwnBasketVirtualGoods
  parent: ExodusOwnBasketWrapper
  components:
  - type: GasTank
    air:
      volume: 10
      temperature: 293.15
      moles:
      - 10
  - type: MaterialStorage
    storage:
      Steel: 300
  - type: BallisticAmmoProvider
    proto: ExodusOwnBasketLeaf
    capacity: 3

- type: entity
  id: ExodusOwnBasketUncertainAmmo
  parent: ExodusOwnBasketWrapper
  components:
  - type: BallisticAmmoProvider
    proto: ExodusOwnBasketRandomPackage
    capacity: 3

- type: entity
  id: ExodusOwnBasketMachineBoard
  parent: ExodusOwnBasketLeaf
  components:
  - type: MachineBoard
    prototype: ExodusOwnBasketMachine
    stackRequirements:
      Steel: 2

- type: entity
  id: ExodusOwnBasketMachine
  parent: ExodusOwnBasketWrapper
  components:
  - type: Machine
    board: ExodusOwnBasketMachineBoard
";

    [Test]
    public async Task OwnBasketLeavesActualContainedStacksToCaller()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var baskets = entities.System<MarketBasketSystem>();
            var containers = entities.System<SharedContainerSystem>();
            var wrapper = entities.SpawnEntity("ExodusOwnBasketWrapper", map.GridCoords);
            var diamond = entities.SpawnEntity("MaterialDiamond1", map.GridCoords);
            entities.System<StackSystem>().SetCount(diamond, 23);
            Assert.That(containers.Insert(diamond, containers.EnsureContainer<Container>(wrapper, "contents")), Is.True);

            Assert.That(baskets.TryGetEntityOwnBasket(wrapper, out var own, out var includesContents, out var failure), Is.True, failure);
            Assert.Multiple(() =>
            {
                Assert.That(includesContents, Is.False);
                Assert.That(own.Lines, Has.Count.EqualTo(1));
                Assert.That(own.Lines[0].MarketKey, Is.EqualTo("proto:ExodusOwnBasketWrapper"));
                Assert.That(own.NominalValue, Is.EqualTo(10));
            });
            Assert.That(baskets.TryGetEntityOwnBasket(diamond, out var child, out includesContents, out failure), Is.True, failure);
            Assert.That(includesContents, Is.False);
            Assert.That(child.Lines, Has.Count.EqualTo(1));
            Assert.That(child.Lines[0].MarketKey, Is.EqualTo("stack:Diamond"));
            Assert.That(child.Lines[0].Quantity, Is.EqualTo(23));

            Assert.That(baskets.TryGetEntityBasket(wrapper, out var recursive, out failure), Is.True, failure);
            Assert.That(recursive.NominalValue, Is.EqualTo(own.NominalValue + child.NominalValue));
            Assert.That(recursive.Lines, Has.Count.EqualTo(2), "The existing recursive appraisal must keep including actual children.");

            var unprototyped = entities.SpawnEntity(null, map.GridCoords);
            Assert.That(baskets.TryGetEntityOwnBasket(unprototyped, out own, out includesContents, out failure), Is.False);
            Assert.That(includesContents, Is.False, "An unprototyped wrapper must not suppress traversal of its children.");
            Assert.That(own.Lines, Is.Empty);
            entities.DeleteEntity(unprototyped);
            entities.DeleteEntity(wrapper);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OwnBasketIncludesLiveGasMaterialsAndUnspawnedAmmunition()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity("ExodusOwnBasketVirtualGoods", map.GridCoords);
            Assert.That(entities.System<MarketBasketSystem>().TryGetEntityOwnBasket(uid, out var own,
                out var includesContents, out var failure), Is.True, failure);
            Assert.Multiple(() =>
            {
                Assert.That(includesContents, Is.False);
                Assert.That(own.Exact, Is.True);
                Assert.That(own.Lines.Single(line => line.MarketKey == DynamicMarketSystem.GasKey(Gas.Oxygen)).Quantity, Is.EqualTo(10));
                Assert.That(own.Lines.Single(line => line.MarketKey == "stack:Steel").Quantity, Is.EqualTo(3));
                Assert.That(own.Lines.Single(line => line.MarketKey == "proto:ExodusOwnBasketLeaf").Quantity, Is.EqualTo(3));
                Assert.That(own.Lines.Single(line => line.MarketKey == "proto:ExodusOwnBasketVirtualGoods").Quantity, Is.EqualTo(1));
                Assert.That(entities.GetComponent<GasTankComponent>(uid).Air.GetMoles(Gas.Oxygen), Is.EqualTo(10),
                    "Reading sale commodities must precede and must not consume the actual contents.");
                Assert.That(entities.GetComponent<MaterialStorageComponent>(uid).Storage["Steel"], Is.EqualTo(300));
                Assert.That(entities.GetComponent<BallisticAmmoProviderComponent>(uid).UnspawnedCount, Is.EqualTo(3));
            });
            entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MachineOwnBasketLeavesItsGeneratedBoardAndMaterialsToCaller()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var machine = entities.SpawnEntity("ExodusOwnBasketMachine", map.GridCoords);
            var component = entities.GetComponent<MachineComponent>(machine);
            Assert.That(component.BoardContainer.ContainedEntities, Has.Count.EqualTo(1));
            Assert.That(component.PartContainer.ContainedEntities, Is.Not.Empty);
            var baskets = entities.System<MarketBasketSystem>();
            Assert.That(baskets.TryGetEntityOwnBasket(machine, out var own, out var includesContents, out var failure), Is.True, failure);
            Assert.Multiple(() =>
            {
                Assert.That(includesContents, Is.False);
                Assert.That(own.Lines, Has.Count.EqualTo(1));
                Assert.That(own.Lines[0].MarketKey, Is.EqualTo("proto:ExodusOwnBasketMachine"));
                Assert.That(own.Lines[0].Quantity, Is.EqualTo(1));
            });
            Assert.That(baskets.TryGetEntityBasket(machine, out var recursive, out failure), Is.True, failure);
            Assert.That(recursive.Lines.Single(line => line.MarketKey == "proto:ExodusOwnBasketMachineBoard").Quantity,
                Is.EqualTo(1));
            Assert.That(recursive.Lines.Single(line => line.MarketKey == "stack:Steel").Quantity, Is.EqualTo(2));
            entities.DeleteEntity(machine);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ExodusOwnBasketPackage", "proto:ExodusOwnBasketLeaf", 2)]
    [TestCase("ExodusOwnBasketRandomPackage", "proto:ExodusOwnBasketRandomPackage", 1)]
    public async Task UnopenedPackagesOwnTheirAppraisedSubtreeWithoutInventingRandomPayloads(
        string prototype, string expectedKey, int expectedQuantity)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity(prototype, map.GridCoords);
            Assert.That(entities.System<MarketBasketSystem>().TryGetEntityOwnBasket(uid, out var own,
                out var includesContents, out var failure), Is.True, failure);
            Assert.Multiple(() =>
            {
                Assert.That(includesContents, Is.True);
                Assert.That(own.Exact, Is.True);
                Assert.That(own.Lines, Has.Count.EqualTo(1));
                Assert.That(own.Lines[0].MarketKey, Is.EqualTo(expectedKey));
                Assert.That(own.Lines[0].Quantity, Is.EqualTo(expectedQuantity));
            });
            entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InexactVirtualAmmunitionCannotBecomeActualSoldQuantities()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var uid = entities.SpawnEntity("ExodusOwnBasketUncertainAmmo", map.GridCoords);
            var baskets = entities.System<MarketBasketSystem>();
            Assert.That(baskets.TryGetEntityBasket(uid, out var recursive, out var failure), Is.True, failure);
            Assert.That(recursive.Exact, Is.False, "The existing recursive API must retain its conservative appraisal bound.");
            Assert.That(baskets.TryGetEntityOwnBasket(uid, out var own, out var includesContents, out failure), Is.False);
            Assert.That(failure, Is.Not.Null);
            Assert.That(includesContents, Is.False);
            Assert.That(own.Lines, Is.Empty, "A purchase bound must never become a committed sale quantity.");
            entities.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }
}
