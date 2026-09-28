using System;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server.Atmos.Components;
using Content.Server.NPC.HTN;
using Content.Server.Shuttles.Components;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Damage;
using Content.Shared.Doors.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task WallConversionKeepsSealAndOrganicDoorOnlyAcceptsRot()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid original = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -3; x <= 5; x++)
                for (var y = -3; y <= 5; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            original = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 1.5f, .5f));
        });
        await pair.RunSeconds(.5f);
        var checks = 0;
        await server.WaitAssertion(() =>
        {
            Assert.That(em.System<RotIntelligentSystem>().TryQueueBuilding((core, em.GetComponent<RotIntelligentComponent>(core)),
                new EntityCoordinates(map.Grid, 1.5f, .5f), "RotBuildWall", 0), Is.True);
            foreach (var job in em.GetComponent<RotColonyStateComponent>(core).Projects)
                checks = (int)Math.Ceiling(em.GetComponent<RotConstructionComponent>(job).Duration.TotalSeconds * 10) + 10;
            Assert.That(checks, Is.GreaterThan(10));
        });
        for (var i = 0; i < checks; i++)
        {
            await pair.RunSeconds(.1f);
            await server.WaitAssertion(() =>
            {
                var sealedTile = false;
                foreach (var uid in em.System<SharedMapSystem>().GetAnchoredEntities(map.Grid, map.Grid.Comp, new Vector2i(1, 0)))
                    sealedTile |= em.TryGetComponent<AirtightComponent>(uid, out var airtight) && airtight.AirBlocked;
                Assert.That(sealedTile, Is.True, "Converting a wall must not open an atmosphere update window.");
            });
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(original), Is.False);
            var door = em.SpawnEntity("RotOrganicDoor", new EntityCoordinates(map.Grid, -1.5f, .5f));
            var human = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, -2.5f, .5f));
            var larva = em.SpawnEntity("MobRotLarva", new EntityCoordinates(map.Grid, -2.5f, 1.5f));
            em.RemoveComponent<HTNComponent>(larva);
            var doors = em.System<SharedDoorSystem>();
            Assert.That(doors.TryOpen(door, user: human), Is.False);
            Assert.That(doors.TryOpen(door, user: larva), Is.True);
            var kidney = em.SpawnEntity("RotKidney", new EntityCoordinates(map.Grid, -.5f, -.5f));
            em.System<RotIntelligentSystem>().Join(kidney, core);
            em.System<RotIntelligentSystem>().TryUnanchor((core, em.GetComponent<RotIntelligentComponent>(core)));
        });
        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            var ship = em.GetComponent<ShuttleComponent>(map.Grid);
            Assert.That(ship.AngularThrusters.Count, Is.EqualTo(1));
            Assert.That(ship.AngularThrust, Is.EqualTo(800));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NesterRemembersObservedViolenceAndDamageInterruptsBothWaitingStates()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid nester = default;
        EntityUid human = default;
        EntityUid wall = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -20; x <= 20; x++)
                for (var y = -5; y <= 5; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            nester = em.SpawnEntity("MobRotNester", new EntityCoordinates(map.Grid, .5f, .5f));
            em.RemoveComponent<HTNComponent>(nester);
            human = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 4.5f, .5f));
            wall = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 5.5f, .5f));
            em.SpawnEntity("RotGrowth", new EntityCoordinates(map.Grid, -8.5f, .5f));
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var system = em.System<RotNesterSystem>();
            var defender = em.GetComponent<RotNesterComponent>(nester);
            var navigation = em.GetComponent<RotDefenderComponent>(nester);
            defender.NextThink = TimeSpan.Zero;
            system.Think((nester, defender));
            Assert.That(navigation.Target, Is.EqualTo(human));
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Heat", FixedPoint2.New(1));
            em.System<DamageableSystem>().TryChangeDamage(wall, damage, true, origin: human);
            Assert.That(system.IsThreat((nester, defender), human), Is.True);
            defender.NextThink = TimeSpan.Zero;
            system.Think((nester, defender));
            Assert.That(navigation.Target, Is.Null);
            Assert.That(navigation.Route, Is.EqualTo(RotDefenderRoute.Cover));
            var movement = em.System<RotDefenderSystem>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            movement.Stop((nester, navigation));
            navigation.Route = RotDefenderRoute.Cover;
            navigation.RestUntil = now + TimeSpan.FromSeconds(14);
            em.System<DamageableSystem>().TryChangeDamage(nester, damage, true);
            defender.NextThink = TimeSpan.Zero;
            system.Think((nester, defender));
            Assert.That(navigation.Route, Is.EqualTo(RotDefenderRoute.Colony));
            Assert.That(navigation.RestUntil, Is.EqualTo(TimeSpan.Zero));
            movement.Stop((nester, navigation));
            navigation.Route = RotDefenderRoute.Colony;
            navigation.RestUntil = now + TimeSpan.FromSeconds(12);
            em.System<DamageableSystem>().TryChangeDamage(nester, damage, true);
            defender.NextThink = TimeSpan.Zero;
            system.Think((nester, defender));
            Assert.That(navigation.Route, Is.EqualTo(RotDefenderRoute.Cover));
            Assert.That(navigation.RestUntil, Is.EqualTo(TimeSpan.Zero));
            movement.Stop((nester, navigation));
        });
        await pair.RunSeconds(21);
        await server.WaitAssertion(() =>
        {
            var defender = em.GetComponent<RotNesterComponent>(nester);
            Assert.That(defender.Aggressors, Is.Empty);
            Assert.That(em.System<RotNesterSystem>().IsThreat((nester, defender), human), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
