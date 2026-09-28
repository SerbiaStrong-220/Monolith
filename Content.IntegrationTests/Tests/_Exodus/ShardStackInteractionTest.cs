using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._Exodus.Stack;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed class ShardStackInteractionTest : InteractionTest
{
    [Test]
    public async Task WeldingConsumesOneShard()
    {
        await SpawnTarget("ShardGlass");
        var shard = STarget.Value;
        await Server.WaitPost(() => Stack.SetCount(shard, 10));
        await InteractUsing(Weld);
        await Server.WaitAssertion(() => Assert.That(Stack.GetCount(shard), Is.EqualTo(9)));
        await FindEntity(("SheetGlass1", 1));
    }

    [Test]
    public async Task CancelledWeldingKeepsAllShards()
    {
        await SpawnTarget("ShardGlass");
        var shard = STarget.Value;
        await Server.WaitPost(() => Stack.SetCount(shard, 10));
        await InteractUsing(Weld, awaitDoAfters: false);
        await CancelDoAfters();
        await Server.WaitAssertion(() => Assert.That(Stack.GetCount(shard), Is.EqualTo(10)));
        await AssertEntityLookup();
    }

    [Test]
    public async Task CraftingFromMenuConsumesOneShard()
    {
        await PlaceInHands(Rod, 10);
        await SpawnEntity((Cable, 10), SEntMan.GetCoordinates(PlayerCoords));
        var shards = await SpawnEntity(("ShardGlass", 10), SEntMan.GetCoordinates(TargetCoords));
        await CraftItem("Spear");
        await FindEntity("Spear");
        await Server.WaitAssertion(() => Assert.That(Stack.GetCount(shards), Is.EqualTo(9)));
    }

    [Test]
    public async Task CraftingDirectlyOnStackPreservesUnusedShards()
    {
        await SpawnTarget("ShardGlass");
        await Server.WaitPost(() => Stack.SetCount(STarget.Value, 10));
        await InteractUsing("MaterialCloth1");
        AssertPrototype("Shiv");
        var remainder = await FindEntity(("ShardGlass", 9));
        await Server.WaitAssertion(() => Assert.That(Stack.GetCount(remainder), Is.EqualTo(9)));
    }

    [Test]
    public async Task ClientUpdatesLayersWhenServerChangesThresholdsWithoutChangingCount()
    {
        await SpawnTarget("ShardGlass");
        await Server.WaitPost(() => Stack.SetCount(STarget.Value, 10));
        await RunTicks(10);
        await Server.WaitPost(() =>
        {
            var uid = STarget.Value;
            var item = SEntMan.GetComponent<StackItemComponent>(uid);
            item.LayerThresholds["stack-few"] = 20;
            item.LayerThresholds["stack-many"] = 5;
            SEntMan.Dirty(uid, item);
        });
        await RunTicks(10);
        await Client.WaitAssertion(() =>
        {
            var uid = CTarget.Value;
            var item = CEntMan.GetComponent<StackItemComponent>(uid);
            var sprites = CEntMan.System<SpriteSystem>();
            Assert.That(sprites.TryGetLayer(uid, "stack-few", out var few, true), Is.True);
            Assert.That(sprites.TryGetLayer(uid, "stack-many", out var many, true), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(CEntMan.GetComponent<StackComponent>(uid).Count, Is.EqualTo(10));
                Assert.That(item.LayerThresholds["stack-few"], Is.EqualTo(20));
                Assert.That(item.LayerThresholds["stack-many"], Is.EqualTo(5));
                Assert.That(few.Visible, Is.False);
                Assert.That(many.Visible, Is.True);
            });
        });
    }

    [Test]
    public async Task ClientDisplaysStackCountAndPileLayers()
    {
        await SpawnTarget("ShardGlass");
        foreach (var count in new[] { 100, 10, 1 })
        {
            await Server.WaitPost(() => Stack.SetCount(STarget.Value, count));
            await RunTicks(10);
            await Client.WaitAssertion(() =>
            {
                var uid = CTarget.Value;
                var sprites = CEntMan.System<SpriteSystem>();
                Assert.That(sprites.TryGetLayer(uid, "stack-few", out var few, true), Is.True);
                Assert.That(sprites.TryGetLayer(uid, "stack-many", out var many, true), Is.True);
                Assert.Multiple(() =>
                {
                    Assert.That(CEntMan.GetComponent<StackComponent>(uid).Count, Is.EqualTo(count));
                    Assert.That(few.Visible, Is.EqualTo(count >= 2));
                    Assert.That(many.Visible, Is.EqualTo(count >= 100));
                });
            });
        }
    }
}
