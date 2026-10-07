// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Shared.Materials;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketGeneratedContentsStockTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusMarketTableFilledContainer
  parent: BaseItem
  components:
  - type: Material
  - type: StaticPrice
    price: 10
  - type: ContainerContainer
    containers:
      test-goods: !type:Container
  - type: EntityTableContainerFill
    containers:
      test-goods: !type:EntSelector
        id: MaterialDiamond1
        amount: !type:ConstantNumberSelector
          value: 7
";

    [TestCase(false)]
    [TestCase(true)]
    public async Task TableGeneratedContentsAreNotStockedAlongsideTheirRecreatedContainer(bool stockContainer)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var inventory = entities.System<MarketInventorySystem>();
            inventory.Clear();
            var root = entities.SpawnEntity("ExodusMarketTableFilledContainer", map.GridCoords);
            try
            {
                if (!stockContainer)
                    entities.RemoveComponent<MaterialComponent>(root);
                var contents = entities.System<SharedContainerSystem>().GetContainer(root, "test-goods");
                Assert.That(contents.ContainedEntities, Is.Not.Empty);
                var child = contents.ContainedEntities[0];
                var sale = new MarketGoodsSoldEvent([child, root], map.Grid.Owner);
                entities.EventBus.RaiseEvent(EventSource.Local, ref sale);
                var duplicateChild = new MarketGoodsSoldEvent([child], map.Grid.Owner);
                entities.EventBus.RaiseEvent(EventSource.Local, ref duplicateChild);

                Assert.That(inventory.TryGetStock("ExodusMarketTableFilledContainer", out var containerStock),
                    Is.EqualTo(stockContainer));
                Assert.That(inventory.TryGetStock("MaterialDiamond1", out var diamondStock), Is.EqualTo(!stockContainer));
                Assert.That(inventory.GetStock(), Has.Count.EqualTo(1));
                if (stockContainer)
                    Assert.That(containerStock!.Quantity, Is.EqualTo(1));
                else
                    Assert.That(diamondStock!.Quantity, Is.EqualTo(7),
                        "Rejecting the outer container must still admit the contents that will be consumed.");
            }
            finally
            {
                entities.DeleteEntity(root);
                inventory.Clear();
            }
        });
        await pair.CleanReturnAsync();
    }
}
