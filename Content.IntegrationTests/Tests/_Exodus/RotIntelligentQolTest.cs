using System;
using System.Collections.Generic;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Damage;
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task CoreCanReturnToItsOwnTissueAndTurningDoesNotRebuildNetwork()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid other = default;
        await server.WaitAssertion(() =>
        {
            for (var x = -2; x <= 14; x++)
                for (var y = -2; y <= 2; y++)
                    em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            other = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, 12.5f, .5f));
            em.GetComponent<RotIntelligentComponent>(core).RootDuration = TimeSpan.FromSeconds(.1);
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            em.System<SharedTransformSystem>().SetLocalRotation(core, Angle.FromDegrees(90));
            Assert.That(brain.NetworkReady, Is.True, "A turn does not cut a one-cell core off from its tissue.");
            Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, brain)), Is.True);
        });
        await pair.RunSeconds(.4f);
        await server.WaitAssertion(() =>
        {
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            Assert.That(brain.Rooted, Is.False);
            em.System<SharedTransformSystem>().SetCoordinates(core, new EntityCoordinates(map.Grid, 1.5f, .5f));
            em.SpawnEntity("RotIntelligentEye", new EntityCoordinates(map.Grid, 1.5f, .5f));
            Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, brain)), Is.True,
                em.GetComponent<RotColonyStateComponent>(core).Feedback);
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            Assert.That(brain.Rooted, Is.True);
            Assert.That(brain.NetworkReady, Is.True);
            Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, brain)), Is.True);
        });
        await pair.RunSeconds(.4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).Rooted, Is.False);
            em.System<SharedTransformSystem>().SetCoordinates(core, new EntityCoordinates(map.Grid, 11.5f, .5f));
            Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, em.GetComponent<RotIntelligentComponent>(core))), Is.False);
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Feedback, Is.EqualTo(Loc.GetString("rot-intelligent-other-colony")));
            Assert.That(em.GetComponent<RotColonyMemberComponent>(other).Core, Is.EqualTo(other));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnsupportedQueuedOrderRefundsItsEntireReservation()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        float biomass = 0;
        await server.WaitAssertion(() =>
        {
            for (var x = -2; x <= 6; x++)
                em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            brain.NetworkBudget = 1;
            brain.BaseIncome = 0;
            biomass = brain.Biomass;
            Assert.That(em.System<RotIntelligentSystem>().TryQueueBuilding((core, brain),
                new EntityCoordinates(map.Grid, 5.5f, .5f), "RotBuildTissue", 0), Is.True);
            Assert.That(brain.Biomass, Is.LessThan(biomass));
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            var state = em.GetComponent<RotColonyStateComponent>(core);
            Assert.That(state.Projects, Is.Empty);
            Assert.That(state.Reservations, Is.Empty);
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).Biomass, Is.EqualTo(biomass));
            Assert.That(state.Cells.ContainsKey(new Vector2i(5, 0)), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GrowthDestroysWindowsAndLampsButRespectsItsRadius()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid window = default;
        EntityUid lamp = default;
        EntityUid crate = default;
        EntityUid outside = default;
        EntityUid core = default;
        await server.WaitAssertion(() =>
        {
            for (var x = -1; x <= 7; x++)
                em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            window = em.SpawnEntity("Window", new EntityCoordinates(map.Grid, 2.5f, .5f));
            lamp = em.SpawnEntity("LightPostSmall", new EntityCoordinates(map.Grid, 3.5f, .5f));
            crate = em.SpawnEntity("CrateGenericSteel", new EntityCoordinates(map.Grid, 4.5f, .5f));
            outside = em.SpawnEntity("Window", new EntityCoordinates(map.Grid, 6.5f, .5f));
            var spread = em.GetComponent<RotSpreadComponent>(core);
            spread.Interval = spread.AttackInterval = TimeSpan.FromSeconds(.1);
            spread.NextGrowth = server.ResolveDependency<IGameTiming>().CurTime;
        });
        await pair.RunSeconds(4);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(window), Is.False, "Windows should break through normal destructible damage.");
            Assert.That(em.EntityExists(lamp), Is.False, "Small non-convertible fixtures also receive damage.");
            Assert.That(em.EntityExists(crate), Is.False, "Movable obstacles should be damaged instead of growing through them.");
            Assert.That(em.GetComponent<DamageableComponent>(outside).TotalDamage.Float(), Is.Zero);
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Connected.Contains(new Vector2i(5, 0)), Is.True);
            Assert.That(em.GetComponent<DamageableComponent>(core).TotalDamage.Float(), Is.Zero);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OverlappingOrgansShareObstacleDamageCooldown()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid window = default;
        await server.WaitAssertion(() =>
        {
            for (var x = -1; x <= 3; x++)
                em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            var core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            var organ = em.SpawnEntity("RotEyeball", new EntityCoordinates(map.Grid, 1.5f, .5f));
            em.System<RotIntelligentSystem>().Join(organ, core);
            window = em.SpawnEntity("Window", new EntityCoordinates(map.Grid, 2.5f, .5f));
            foreach (var uid in new[] { core, organ })
            {
                var spread = em.GetComponent<RotSpreadComponent>(uid);
                spread.Interval = TimeSpan.FromSeconds(.05);
                spread.AttackInterval = TimeSpan.FromSeconds(1);
                spread.NextGrowth = server.ResolveDependency<IGameTiming>().CurTime;
            }
        });
        await pair.RunSeconds(.7f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(window), Is.True, "Two overlapping organs must not deal two hits in the same cooldown.");
            Assert.That(em.GetComponent<DamageableComponent>(window).TotalDamage.Float(), Is.EqualTo(30));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NurseryReleasesThreeCarriersInOneBatch()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid nursery = default;
        await server.WaitAssertion(() =>
        {
            for (var x = -2; x <= 4; x++)
                for (var y = -2; y <= 4; y++)
                    em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            // Select the carrier branch from the real table without relying on random test outcomes.
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            ProtoId<EntityTablePrototype> tableId = "RotNurseryOffspring";
            var table = (GroupSelector)prototypes.Index(tableId).Table;
            foreach (var branch in table.Children)
            {
                if (branch is not EntSelector { Id.Id: "MobRotSpawn" })
                    continue;
                nursery = em.CreateEntityUninitialized("RotNursery", new EntityCoordinates(map.Grid, -.5f, -.5f));
                var brood = em.GetComponent<RotNurseryComponent>(nursery);
                brood.Offspring = branch;
                brood.Duration = TimeSpan.FromSeconds(.1);
                em.InitializeAndStartEntity(nursery, map.MapId);
                Assert.That(brood.RemainingOffspring.Count, Is.EqualTo(2));
                break;
            }
            Assert.That(em.EntityExists(nursery), Is.True);
            Assert.That(em.System<RotIntelligentSystem>().Join(nursery, core), Is.True);
        });
        await pair.RunSeconds(.8f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(nursery), Is.False);
            var children = em.EntityQueryEnumerator<RotColonyMemberComponent, MetaDataComponent>();
            var count = 0;
            var positions = new HashSet<EntityCoordinates>();
            while (children.MoveNext(out var uid, out var member, out var metadata))
            {
                if (metadata.EntityPrototype?.ID != "MobRotSpawn")
                    continue;
                Assert.That(member.Core, Is.EqualTo(core));
                positions.Add(em.GetComponent<TransformComponent>(uid).Coordinates);
                count++;
            }
            Assert.That(count, Is.EqualTo(3));
            Assert.That(positions.Count, Is.EqualTo(3), "Children need separate free exit positions.");
        });
        await pair.CleanReturnAsync();
    }
}
