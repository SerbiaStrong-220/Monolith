using System.Collections.Generic;
using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Atmos.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Projectiles;
using Content.Shared.Spider;
using Content.Shared.Sprite;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task HulkChangesAreReversibleAndDoNotStack(bool removeEffects)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var gun = entities.SpawnEntity("WeaponPistolMk58", new EntityCoordinates(map, Vector2.Zero));
            var gunComponent = entities.GetComponent<GunComponent>(gun);
            var scale = entities.System<SharedScaleVisualsSystem>();
            var baseScale = new Vector2(0.9f, 1.1f);
            scale.SetSpriteScale(body, baseScale, relativeToOriginal: removeEffects);
            var fixtures = entities.GetComponent<FixturesComponent>(body);
            var radii = new Dictionary<string, float>();
            foreach (var (id, fixture) in fixtures.Fixtures)
            {
                if (fixture.Shape is PhysShapeCircle circle)
                    radii.Add(id, circle.Radius);
            }
            Assert.That(radii, Is.Not.Empty);

            var genetics = entities.System<GeneticsSystem>();
            var genome = Enable(entities, body, "GeneticHulk");
            Enable(entities, body, "GeneticHulk");
            Assert.That(Vector2.Distance(scale.GetSpriteScale(body), baseScale * 1.15f), Is.LessThan(0.0001f));
            Assert.That(entities.GetComponent<ScaleVisualsComponent>(body).RelativeToOriginal, Is.EqualTo(removeEffects));
            foreach (var (id, radius) in radii)
                Assert.That(((PhysShapeCircle) fixtures.Fixtures[id].Shape).Radius, Is.EqualTo(radius * 1.15f).Within(0.0001f));
            var shot = new ShotAttemptedEvent { User = body, Used = (gun, gunComponent) };
            entities.EventBus.RaiseLocalEvent(body, ref shot);
            Assert.That(shot.Cancelled, Is.True);

            var hunger = entities.GetComponent<HungerComponent>(body);
            var thirst = entities.GetComponent<ThirstComponent>(body);
            var hungerSystem = entities.System<HungerSystem>();
            var thirstSystem = entities.System<ThirstSystem>();
            hungerSystem.SetHunger(body, 140f, hunger);
            thirstSystem.SetThirst(body, thirst, 400f);
            var food = hungerSystem.GetHunger(hunger);
            var water = thirst.CurrentThirst;
            var extraFood = hunger.ActualDecayRate * 0.5f * (float) genome.Interval.TotalSeconds;
            var extraWater = thirst.ActualDecayRate * 0.5f * (float) (genome.Interval.TotalSeconds / thirst.UpdateRate.TotalSeconds);
            genome.NextUpdate = TimeSpan.Zero;
            genetics.Update(0);
            Assert.That(food - hungerSystem.GetHunger(hunger), Is.EqualTo(extraFood).Within(0.0001f));
            Assert.That(water - thirst.CurrentThirst, Is.EqualTo(extraWater).Within(0.0001f));

            if (removeEffects)
                entities.RemoveComponent<GeneticEffectsComponent>(body);
            else
                Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(Vector2.Distance(scale.GetSpriteScale(body), baseScale), Is.LessThan(0.0001f));
            Assert.That(entities.GetComponent<ScaleVisualsComponent>(body).RelativeToOriginal, Is.EqualTo(removeEffects));
            foreach (var (id, radius) in radii)
                Assert.That(((PhysShapeCircle) fixtures.Fixtures[id].Shape).Radius, Is.EqualTo(radius).Within(0.0001f));
            shot = new ShotAttemptedEvent { User = body, Used = (gun, gunComponent) };
            entities.EventBus.RaiseLocalEvent(body, ref shot);
            Assert.That(shot.Cancelled, Is.False);
            food = hungerSystem.GetHunger(hunger);
            water = thirst.CurrentThirst;
            genome.NextUpdate = TimeSpan.Zero;
            genetics.Update(0);
            Assert.That(hungerSystem.GetHunger(hunger), Is.EqualTo(food));
            Assert.That(thirst.CurrentThirst, Is.EqualTo(water));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ArachnidsStartWithWebGlandsAndKeepNativeWebImmunityAfterReset()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobArachnid", new EntityCoordinates(map, Vector2.Zero));
            var genome = entities.GetComponent<GenomeComponent>(body);
            Assert.That(genome.Active.Contains("GeneticWeb"), Is.True);
            Assert.That(genome.Actions.ContainsKey("ActionGeneticWeb"), Is.True);
            Assert.That(entities.HasComponent<IgnoreSpiderWebComponent>(body), Is.True);
            Assert.That(entities.System<GeneticsSystem>().TryStabilize(body), Is.True);
            Assert.That(genome.Active.Contains("GeneticWeb"), Is.False);
            Assert.That(entities.HasComponent<IgnoreSpiderWebComponent>(body), Is.True);
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PassiveDeflectionUsesSeparateReflectionSourceAndStopsAfterReset()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var innate = entities.AddComponent<ReflectComponent>(body);
            innate.ReflectProb = 0;
            var genome = Enable(entities, body, "GeneticJump");
            Assert.That(genome.Actions.ContainsKey("ActionGeneticJump"), Is.False);
            var state = entities.GetComponent<GeneticAbilityStateComponent>(body);
            Assert.That(state.Deflector, Is.Not.Null);
            var reflection = entities.GetComponent<ReflectComponent>(state.Deflector!.Value);
            Assert.That(reflection.ReflectProb, Is.EqualTo(0.1f));
            // Force the two probability branches instead of using a flaky statistical test.
            reflection.ReflectProb = 1;
            var ray = new HitScanReflectAttemptEvent(null, body, ReflectType.Energy, Vector2.UnitX, false, null);
            entities.EventBus.RaiseLocalEvent(body, ref ray);
            Assert.That(ray.Reflected, Is.True);
            Assert.That(ray.Direction.X, Is.LessThan(0));

            var projectile = entities.SpawnEntity("ProjectileGeneticFlame", new EntityCoordinates(map, new Vector2(3, 0)));
            entities.AddComponent<ReflectiveComponent>(projectile);
            entities.System<SharedGunSystem>().ShootProjectile(projectile, Vector2.UnitX, Vector2.Zero, body, body);
            var projectileHit = new ProjectileReflectAttemptEvent(projectile, entities.GetComponent<ProjectileComponent>(projectile), false);
            entities.EventBus.RaiseLocalEvent(body, ref projectileHit);
            Assert.That(projectileHit.Cancelled, Is.True);
            Assert.That(entities.GetComponent<PhysicsComponent>(projectile).LinearVelocity.X, Is.LessThan(0));

            reflection.ReflectProb = 0;
            ray = new HitScanReflectAttemptEvent(null, body, ReflectType.Energy, Vector2.UnitX, false, null);
            entities.EventBus.RaiseLocalEvent(body, ref ray);
            Assert.That(ray.Reflected, Is.False);
            projectileHit = new ProjectileReflectAttemptEvent(projectile, entities.GetComponent<ProjectileComponent>(projectile), false);
            entities.EventBus.RaiseLocalEvent(body, ref projectileHit);
            Assert.That(projectileHit.Cancelled, Is.False);
            reflection.ReflectProb = 1;
            Assert.That(entities.System<GeneticsSystem>().TryStabilize(body), Is.True);
            Assert.That(state.Deflector, Is.Null);
            ray = new HitScanReflectAttemptEvent(null, body, ReflectType.Energy, Vector2.UnitX, false, null);
            entities.EventBus.RaiseLocalEvent(body, ref ray);
            Assert.That(ray.Reflected, Is.False);
            projectileHit = new ProjectileReflectAttemptEvent(projectile, entities.GetComponent<ProjectileComponent>(projectile), false);
            entities.EventBus.RaiseLocalEvent(body, ref projectileHit);
            Assert.That(projectileHit.Cancelled, Is.False);
            Assert.That(entities.GetComponent<ReflectComponent>(body), Is.SameAs(innate));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FireBreathDoesNotIgniteItsCarrier()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genome = Enable(entities, body, "GeneticFireBreath");
            entities.System<HungerSystem>().SetHunger(body, 150f);
            var fire = entities.GetComponent<FlammableComponent>(body);
            var stacks = fire.FireStacks;
            var breath = new GeneticFireBreathEvent { Performer = body, Target = new EntityCoordinates(map, new Vector2(3, 0)) };
            entities.EventBus.RaiseLocalEvent(body, breath);
            Assert.That(breath.Handled, Is.True);
            Assert.That(fire.OnFire, Is.False);
            Assert.That(fire.FireStacks, Is.EqualTo(stacks));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
