using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server.NPC.Components;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class RotHungryRetreatTest
{
    private sealed class LiveContext : ITestContextLike
    {
        public string FullName { get; } = TestContext.CurrentContext.Test.FullName;
        public TextWriter Out { get; } = TextWriter.Synchronized(TestContext.Progress);
    }

    [TestCase(30f)]
    [TestCase(6f)]
    public async Task RetreatContinuesForFiveSecondsBeforeTurningOnPursuer(float range)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid hungry = default;
        EntityUid pursuer = default;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CVars.NetTickrate, 30);
            var maps = em.System<SharedMapSystem>();
            for (var x = -8; x <= 60; x++)
            {
                for (var y = -1; y <= 1; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            var gravity = em.EnsureComponent<GravityComponent>(map.Grid);
            gravity.Inherent = gravity.Enabled = true;
            hungry = em.SpawnEntity("MobRotHungry", new EntityCoordinates(map.Grid, .5f, .5f));
            pursuer = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, -3.5f, .5f));
            var comp = em.GetComponent<RotHungryComponent>(hungry);
            comp.Regeneration = 0;
            comp.RetreatRange = range;
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Heat", FixedPoint2.New(350));
            em.System<DamageableSystem>().TryChangeDamage(hungry, damage, true, origin: pursuer);
            em.GetComponent<RotHungryComponent>(hungry).NextThink = server.ResolveDependency<IGameTiming>().CurTime;
        });
        await pair.RunSeconds(.3f);
        TimeSpan deadline = default;
        float lastX = .5f;
        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<RotHungryComponent>(hungry);
            Assert.That(comp.Retreating, Is.True);
            deadline = comp.RetreatUntil;
        });
        for (var sample = 0; sample < 10; sample++)
        {
            await pair.RunSeconds(.4f);
            await server.WaitAssertion(() =>
            {
                var comp = em.GetComponent<RotHungryComponent>(hungry);
                var position = em.GetComponent<TransformComponent>(hungry).Coordinates;
                Assert.That(comp.Retreating, Is.True, "Reaching a waypoint must not end the retreat early.");
                Assert.That(comp.Frenzied, Is.False);
                Assert.That(comp.RetreatUntil, Is.EqualTo(deadline), "Extending a route must not restart the five-second timer.");
                Assert.That(position.X, Is.GreaterThanOrEqualTo(lastX - .1f), "Retreat must not reverse towards the shooter.");
                if (sample >= 3)
                    Assert.That(position.X - lastX, Is.GreaterThan(.3f), "Continue moving through intermediate waypoints.");
                lastX = position.X;
                em.System<SharedTransformSystem>().SetCoordinates(pursuer, new EntityCoordinates(map.Grid, position.X - 4, .5f));
            });
        }
        await server.WaitAssertion(() => Assert.That(lastX, Is.GreaterThan(16), "Use the retreat window to open a substantial distance."));
        await pair.RunSeconds(1.1f);
        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<RotHungryComponent>(hungry);
            Assert.That(comp.Retreating, Is.False);
            Assert.That(comp.Frenzied, Is.True, "An active pursuer should trigger the existing counterattack after five seconds.");
            Assert.That(comp.Target, Is.EqualTo(pursuer));
            Assert.That(comp.RetreatPath, Is.Null);
            Assert.That(comp.RetreatCancellation, Is.Null);
            Assert.That(em.GetComponent<NPCMeleeCombatComponent>(hungry).Target, Is.EqualTo(pursuer));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RetreatDoesNotRunThroughShooterAndCanBreakOut(bool blocked)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid hungry = default;
        EntityUid pursuer = default;
        var walls = new List<EntityUid>();
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CVars.NetTickrate, 30);
            var maps = em.System<SharedMapSystem>();
            for (var x = -40; x <= (blocked ? 40 : 4); x++)
            {
                for (var y = -1; y <= 1; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            var gravity = em.EnsureComponent<GravityComponent>(map.Grid);
            gravity.Inherent = gravity.Enabled = true;
            if (blocked)
            {
                for (var y = -1; y <= 1; y++)
                    walls.Add(em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 4.5f, y + .5f)));
            }
            hungry = em.SpawnEntity("MobRotHungry", new EntityCoordinates(map.Grid, .5f, .5f));
            pursuer = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, -3.5f, .5f));
            em.GetComponent<RotHungryComponent>(hungry).Regeneration = 0;
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Heat", FixedPoint2.New(350));
            em.System<DamageableSystem>().TryChangeDamage(hungry, damage, true, origin: pursuer);
            em.GetComponent<RotHungryComponent>(hungry).NextThink = server.ResolveDependency<IGameTiming>().CurTime;
        });
        for (var i = 0; i < 4; i++)
        {
            await pair.RunSeconds(1);
            await server.WaitAssertion(() =>
            {
                var transform = em.GetComponent<TransformComponent>(hungry);
                Assert.That(transform.GridUid, Is.EqualTo(map.Grid.Owner), "Do not overshoot the retreat waypoint into space.");
                Assert.That(transform.Coordinates.X, Is.GreaterThanOrEqualTo(.4f),
                    "The longest corridor is behind the shooter: do not retreat through them.");
            });
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(hungry).Coordinates.X, Is.GreaterThan(2));
            if (blocked)
                Assert.That(walls.Any(wall => !em.EntityExists(wall)
                    || em.GetComponent<DamageableComponent>(wall).TotalDamage > FixedPoint2.Zero), Is.True,
                    "With no open escape route, preserve the hungry's ability to smash obstacles.");
        });
        await pair.CleanReturnAsync();
    }
}
