using System;
using System.IO;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC;
using Content.Shared.Projectiles;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class RotRetaliationTest
{
    private sealed class LiveContext : ITestContextLike
    {
        public string FullName { get; } = TestContext.CurrentContext.Test.FullName;
        public TextWriter Out { get; } = TextWriter.Synchronized(TestContext.Progress);
    }

    [Test]
    public async Task DeferredMeleeStopDoesNotResumeWhenAnotherBehaviorEnablesCombat()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var creature = em.SpawnEntity("MobRotHungry", map.GridCoords);
            var target = em.SpawnEntity("WeaponTurretSyndicate", map.GridCoords);
            em.EnsureComponent<ActiveNPCComponent>(creature);
            em.EnsureComponent<NPCMeleeCombatComponent>(creature).Target = target;
            em.RemoveComponentDeferred<NPCMeleeCombatComponent>(creature);

            // Ground strikes enable combat mode after stopping the regular melee controller.
            em.System<SharedCombatModeSystem>().SetInCombatMode(creature, true);
            var combat = em.System<NPCCombatSystem>();
            combat.Update(0);
            Assert.That(em.HasComponent<NPCSteeringComponent>(creature), Is.False,
                "A stopped melee controller must not reclaim movement during the same tick.");

            var resumed = em.EnsureComponent<NPCMeleeCombatComponent>(creature);
            resumed.Target = target;
            combat.Update(0);
            Assert.That(em.HasComponent<NPCSteeringComponent>(creature), Is.True,
                "Explicitly starting a new melee controller in the same tick must still work.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PlayerControlAndDeathClearRetaliation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid creature = default;
        EntityUid turret = default;
        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Heat", FixedPoint2.New(1));
        await server.WaitAssertion(() =>
        {
            creature = em.SpawnEntity("MobRotHungry", map.GridCoords);
            turret = em.SpawnEntity("WeaponTurretSyndicate", map.GridCoords);
            em.System<HTNSystem>().SetHTNEnabled((turret, em.GetComponent<HTNComponent>(turret)), false);
            em.System<DamageableSystem>().TryChangeDamage(creature, damage, true, origin: turret);
            var response = em.GetComponent<RotRetaliationComponent>(creature);
            Assert.That(response.Target, Is.EqualTo(turret));
            server.PlayerMan.SetAttachedEntity(pair.Player!, creature);
            Assert.That(response.Target, Is.Null);
            em.System<DamageableSystem>().TryChangeDamage(creature, damage, true, origin: turret);
            Assert.That(response.Target, Is.Null, "An NPC reaction must not take over a player-controlled ghost role.");
            var other = em.SpawnEntity("MobRotSated", map.GridCoords);
            em.System<DamageableSystem>().TryChangeDamage(other, damage, true, origin: turret);
            var otherResponse = em.GetComponent<RotRetaliationComponent>(other);
            Assert.That(otherResponse.Target, Is.EqualTo(turret));
            em.System<MobStateSystem>().ChangeMobState(other, MobState.Dead);
            Assert.That(otherResponse.Target, Is.Null);
            Assert.That(otherResponse.PathCancellation, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("MobRotHungry", false)]
    [TestCase("MobRotSated", false)]
    [TestCase("MobRotSpawn", false)]
    [TestCase("MobRotHungry", true)]
    [TestCase("MobRotSated", true)]
    [TestCase("MobRotSpawn", true)]
    public async Task RetaliatesAgainstTurretOrEscapesAfterFourSeconds(string prototype, bool unreachable)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid creature = default;
        EntityUid turret = default;
        EntityUid projectile = default;
        EntityUid secondTurret = default;
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(CVars.NetTickrate, 30);
            var maps = em.System<SharedMapSystem>();
            for (var x = -55; x <= (unreachable ? 0 : 7); x++)
            {
                for (var y = -1; y <= 1; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(7, 0), map.Tile.Tile);
            var gravity = em.EnsureComponent<GravityComponent>(map.Grid);
            gravity.Inherent = gravity.Enabled = true;
            creature = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
            turret = em.SpawnEntity("WeaponTurretSyndicate", new EntityCoordinates(map.Grid, 7.5f, .5f));
            em.System<HTNSystem>().SetHTNEnabled((turret, em.GetComponent<HTNComponent>(turret)), false);
            if (unreachable)
            {
                secondTurret = em.SpawnEntity("WeaponTurretSyndicate", new EntityCoordinates(map.Grid, 7.5f, .5f));
                em.System<HTNSystem>().SetHTNEnabled((secondTurret, em.GetComponent<HTNComponent>(secondTurret)), false);
            }
            if (em.TryGetComponent<RotHungryComponent>(creature, out var hungry))
                hungry.Regeneration = 0;
            // Exercise the damage tool fallback as well as direct shooter attribution.
            projectile = em.SpawnEntity(null, new EntityCoordinates(map.Grid, .5f, 8.5f));
            em.AddComponent<ProjectileComponent>(projectile).Weapon = turret;
        });
        await pair.RunSeconds(1);
        TimeSpan deadline = default;
        await server.WaitAssertion(() =>
        {
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Heat", FixedPoint2.New(1));
            em.System<DamageableSystem>().TryChangeDamage(creature, damage, true, tool: projectile);
            var response = em.GetComponent<RotRetaliationComponent>(creature);
            Assert.That(response.Target, Is.EqualTo(turret));
            deadline = response.ReachBy;
        });
        for (var i = 0; i < 7; i++)
        {
            await pair.RunSeconds(.5f);
            await server.WaitAssertion(() =>
            {
                var response = em.GetComponent<RotRetaliationComponent>(creature);
                Assert.That(response.Retreating, Is.False);
                if (unreachable)
                    Assert.That(response.ReachBy, Is.EqualTo(deadline), "Repeated shots must not extend the attempt.");
                var damage = new DamageSpecifier();
                damage.DamageDict.Add("Heat", FixedPoint2.New(1));
                em.System<DamageableSystem>().TryChangeDamage(creature, damage, true, origin: turret);
            });
        }
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var response = em.GetComponent<RotRetaliationComponent>(creature);
            Assert.That(response.Retreating, Is.EqualTo(unreachable));
            if (unreachable)
                Assert.That(em.HasComponent<NPCMeleeCombatComponent>(creature), Is.False);
            else
                Assert.That(em.GetComponent<DamageableComponent>(turret).TotalDamage, Is.GreaterThan(FixedPoint2.Zero),
                    "The native NPC must actually hit the turret, including infection carriers.");
        });
        if (unreachable)
        {
            for (var i = 0; i < 12; i++)
            {
                await pair.RunSeconds(.25f);
                await server.WaitAssertion(() =>
                {
                    var damage = new DamageSpecifier();
                    damage.DamageDict.Add("Heat", FixedPoint2.New(1));
                    em.System<DamageableSystem>().TryChangeDamage(creature, damage, true,
                        origin: i % 2 == 0 ? turret : secondTurret);
                });
            }
            await server.WaitAssertion(() =>
            {
                var position = em.GetComponent<TransformComponent>(creature);
                Assert.That(position.GridUid, Is.EqualTo(map.Grid.Owner));
                Assert.That(position.Coordinates.X, Is.LessThan(-3), "Retreat must actually move away from the turret.");
                Assert.That(em.GetComponent<RotRetaliationComponent>(creature).Retreating, Is.True);
            });
        }
        await server.WaitAssertion(() =>
        {
            em.DeleteEntity(turret);
            if (unreachable)
                em.DeleteEntity(secondTurret);
        });
        await pair.RunSeconds(1);
        await server.WaitAssertion(() =>
        {
            var response = em.GetComponent<RotRetaliationComponent>(creature);
            Assert.That(response.Target, Is.Null);
            Assert.That(response.Path, Is.Null);
            Assert.That(response.PathCancellation, Is.Null);
            Assert.That(response.Retreating, Is.False);
        });
        await pair.CleanReturnAsync();
    }
}
