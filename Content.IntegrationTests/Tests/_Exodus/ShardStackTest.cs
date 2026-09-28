using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Server._Exodus.Destructible;
using Content.Server._Exodus.Stack;
using Content.Server.Destructible;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Kitchen.Components;
using Content.Server.Stack;
using Content.Shared._Exodus.Stack;
using Content.Shared.Damage;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen;
using Content.Shared.Stacks;
using Robust.Shared.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class ShardStackTest
{
    private sealed class LiveContext : ITestContextLike
    {
        public string FullName { get; } = TestContext.CurrentContext.Test.FullName;
        public TextWriter Out { get; } = TextWriter.Synchronized(TestContext.Progress);
    }

    [TestPrototypes]
    private const string Prototypes = """
        - type: explosion
          id: ShardStackTestExplosion
          damagePerIntensity:
            types:
              Blunt: 50
          tileBreakChance: [0, 0]
          tileBreakIntensity: [0, 1]
          throwEntitiesOnExplosion: false
        """;

    [TestCase(0, 0, 0, 0)]
    [TestCase(1, 0, 0, 1)]
    [TestCase(9, 0, 0, 9)]
    [TestCase(10, 0, 1, 0)]
    [TestCase(11, 0, 1, 1)]
    [TestCase(70, 0, 7, 0)]
    [TestCase(77, 0, 7, 7)]
    [TestCase(99, 0, 9, 9)]
    [TestCase(100, 1, 0, 0)]
    [TestCase(177, 1, 7, 7)]
    public async Task DestructionPreservesCountAndDenominations(int amount, int hundreds, int tens, int singles)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var sheet = em.SpawnEntity(amount == 0 ? "SheetGlassLingering0" : "SheetGlass", map.GridCoords);
            if (amount > 100)
                em.GetComponent<StackComponent>(sheet).MaxCountOverride = amount;
            em.System<StackSystem>().SetCount(sheet, amount);
            Break(em, sheet);

            var drops = GetDrops(em, map.GridCoords);
            Assert.Multiple(() =>
            {
                Assert.That(drops.Count(x => x.Comp.Count == 100), Is.EqualTo(hundreds));
                Assert.That(drops.Count(x => x.Comp.Count == 10), Is.EqualTo(tens));
                Assert.That(drops.Count(x => x.Comp.Count == 1), Is.EqualTo(singles));
                Assert.That(drops.Sum(x => x.Comp.Count), Is.EqualTo(amount));
                Assert.That(drops, Has.Count.EqualTo(hundreds + tens + singles));
            });
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("SheetRGlass", "ReinforcedGlassShard")]
    [TestCase("SheetPGlass", "PlasmaGlassShard")]
    [TestCase("SheetRPGlass", "PlasmaGlassShard")]
    [TestCase("SheetUGlass", "UraniumGlassShard")]
    [TestCase("SheetRUGlass", "UraniumGlassShard")]
    [TestCase("SheetClockworkGlass", "ClockworkGlassShard")]
    public async Task GlassVariantsUseTheirOwnShardStacks(string sheetId, string stackType)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var sheet = em.SpawnEntity(sheetId, map.GridCoords);
            em.System<StackSystem>().SetCount(sheet, 77);
            Break(em, sheet);
            var drops = GetDrops(em, map.GridCoords);
            Assert.Multiple(() =>
            {
                Assert.That(drops, Has.Count.EqualTo(14));
                Assert.That(drops.Sum(x => x.Comp.Count), Is.EqualTo(77));
                Assert.That(drops.All(x => x.Comp.StackTypeId == stackType), Is.True);
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HundredFullStacksCreateHundredEntities()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var stacks = em.System<StackSystem>();
            for (var i = 0; i < 100; i++)
            {
                var sheet = em.SpawnEntity("SheetGlass", map.GridCoords);
                stacks.SetCount(sheet, 100);
                Break(em, sheet);
            }

            var drops = GetDrops(em, map.GridCoords);
            Assert.Multiple(() =>
            {
                Assert.That(drops, Has.Count.EqualTo(100));
                Assert.That(drops.Sum(x => x.Comp.Count), Is.EqualTo(10000));
                Assert.That(drops.All(x => x.Comp.Count == 100), Is.True);
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExplosionCreatesFilledStacksWithoutAnIntermediateShardBurst()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        var sheets = new List<EntityUid>();
        await pair.Server.WaitAssertion(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                var sheet = em.SpawnEntity("SheetGlass", map.GridCoords);
                em.System<StackSystem>().SetCount(sheet, 100);
                sheets.Add(sheet);
            }

            em.System<ExplosionSystem>().QueueExplosion(map.GridCoords, "ShardStackTestExplosion",
                1, 1, 1, null, tileBreakScale: 0, canCreateVacuum: false);
        });
        await pair.RunSeconds(2);
        await pair.Server.WaitAssertion(() =>
        {
            var drops = GetDrops(em, map.GridCoords);
            Assert.Multiple(() =>
            {
                Assert.That(sheets.All(em.Deleted), Is.True);
                Assert.That(drops, Has.Count.EqualTo(100));
                Assert.That(drops.Sum(x => x.Comp.Count), Is.EqualTo(10000));
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SavedGridRetainsShardCountAndCanStillSplit()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            em.System<StackSystem>().Spawn(77, "GlassShard", map.GridCoords);
            var loader = em.System<MapLoaderSystem>();
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid.Owner, writer), Is.True);
            em.DeleteEntity(map.Grid.Owner);
            using var reader = new StringReader(writer.ToString());
            Assert.That(loader.TryLoadGrid(reader, "shard-stack-save-test", out _, out var grid,
                DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
            var drops = GetDrops(em, new EntityCoordinates(grid.Value.Owner, 0.5f, 0.5f));
            Assert.That(drops, Has.Count.EqualTo(1));
            var shard = drops[0];
            Assert.That(shard.Comp.Count, Is.EqualTo(77));
            Assert.That(em.System<StackItemSystem>().TryTakeOne(shard, out _), Is.True);
            Assert.That(shard.Comp.Count, Is.EqualTo(76));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TriggerConsumesOneAndSplittingAndMergingPreserveRemainder()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid shard = default;
        await pair.Server.WaitAssertion(() =>
        {
            var stacks = em.System<StackSystem>();
            shard = stacks.Spawn(10, "GlassShard", map.GridCoords);
            var triggers = em.System<TriggerSystem>();
            triggers.Trigger(shard);
            Assert.That(stacks.GetCount(shard), Is.EqualTo(9));

            var split = stacks.Split(shard, 3, map.GridCoords);
            Assert.That(split, Is.Not.Null);
            Assert.That(stacks.GetCount(shard), Is.EqualTo(6));
            Assert.That(stacks.TryAdd(split.Value, shard), Is.True);
            Assert.That(stacks.GetCount(shard), Is.EqualTo(9));

            for (var i = 0; i < 9; i++)
                triggers.Trigger(shard);
            Assert.That(em.IsQueuedForDeletion(shard), Is.True);
        });
        await pair.Server.WaitRunTicks(1);
        await pair.Server.WaitAssertion(() => Assert.That(em.Deleted(shard), Is.True));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HighDamageStillDestroysGlassWithoutDrops()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var sheet = em.SpawnEntity("SheetGlass", map.GridCoords);
            Break(em, sheet, 100);
            Assert.That(GetDrops(em, map.GridCoords), Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(0, 88, 60)]
    [TestCase(1, 89, 56)]
    [TestCase(59, 100, 59)]
    [TestCase(60, 100, 60)]
    public async Task GrinderProcessesOnlyTheShardsThatFitInTheBeaker(int initialVolume, int remaining, int finalVolume)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid shard = default;
        EntityUid beaker = default;
        await pair.Server.WaitAssertion(() =>
        {
            var grinder = em.SpawnEntity("KitchenReagentGrinder", map.GridCoords);
            shard = em.System<StackSystem>().Spawn(100, "GlassShard", map.GridCoords);
            beaker = em.SpawnEntity("Beaker", map.GridCoords);
            var solutions = em.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out var beakerSolution, out _), Is.True);
            if (initialVolume > 0)
                Assert.That(solutions.TryAddReagent(beakerSolution.Value, "Water", FixedPoint2.New(initialVolume)), Is.True);
            var containers = em.System<SharedContainerSystem>();
            Assert.That(containers.Insert(shard,
                containers.GetContainer(grinder, SharedReagentGrinder.InputContainerId)), Is.True);
            Assert.That(containers.Insert(beaker,
                containers.GetContainer(grinder, SharedReagentGrinder.BeakerSlotId)), Is.True);
            em.AddComponent<ActiveReagentGrinderComponent>(grinder);
        });
        await pair.Server.WaitRunTicks(2);
        await pair.Server.WaitAssertion(() =>
        {
            var solutions = em.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(beaker, "beaker", out _, out var solution), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(solution.Volume, Is.EqualTo(FixedPoint2.New(finalVolume)));
                Assert.That(em.System<StackSystem>().GetCount(shard), Is.EqualTo(remaining));
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DropConfigurationIsValidAndRejectsInvalidDenominations()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var system = em.System<StackSpawnSystem>();
            foreach (var prototype in pair.Server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (!prototype.TryGetComponent<DestructibleComponent>(out var component, em.ComponentFactory))
                    continue;

                foreach (var threshold in component.Thresholds)
                    foreach (var behavior in threshold.Behaviors.OfType<SpawnStackBehavior>())
                        Assert.That(system.IsValid(behavior), Is.True, prototype.ID);
            }

            foreach (var sizes in new[] { new List<int>(), new List<int> { 0, 1 }, new List<int> { 10 },
                         new List<int> { 1, 10 }, new List<int> { 101, 1 }, new List<int> { 10, 10, 1 } })
                Assert.That(system.IsValid(new SpawnStackBehavior { Stack = "GlassShard", Sizes = sizes }), Is.False);

            Assert.That(system.IsValid(new SpawnStackBehavior
            { Stack = "GlassShard", Sizes = new List<int> { 100, 10, 1 }, AmountPerUnit = 0 }), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    private static void Break(IEntityManager em, EntityUid entity, int amount = 50)
    {
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", FixedPoint2.New(amount));
        em.System<DamageableSystem>().TryChangeDamage(entity, damage, ignoreResistances: true,
            ignoreGlobalModifiers: true);
    }

    private static List<Entity<StackComponent>> GetDrops(IEntityManager em, EntityCoordinates coordinates)
    {
        var result = new List<Entity<StackComponent>>();
        foreach (var uid in em.System<EntityLookupSystem>().GetEntitiesInRange(coordinates, 2, LookupFlags.All))
        {
            if (em.HasComponent<StackItemComponent>(uid) && !em.IsQueuedForDeletion(uid) &&
                em.TryGetComponent<StackComponent>(uid, out var stack))
                result.Add((uid, stack));
        }
        return result;
    }
}
