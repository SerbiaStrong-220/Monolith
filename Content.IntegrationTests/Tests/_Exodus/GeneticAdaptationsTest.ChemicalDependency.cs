#nullable enable
using System.Numerics;
using Content.Server._Exodus.Chemistry;
using Content.Server._Exodus.Genetics;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Exodus.Chemistry;
using Content.Shared.Alert;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [TestCase(0.1, 0.4, 6)]
    [TestCase(5, 20, 300)]
    [TestCase(30, 30, 1800)]
    public async Task GoJuiceCreditsOnlyTheMetabolizedDose(double units, double boostSeconds, double reserveSeconds)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobAsakim", new EntityCoordinates(map, Vector2.Zero));
            var genome = entities.GetComponent<GenomeComponent>(body);
            var dependency = entities.GetComponent<ChemicalDependencyComponent>(body);
            dependency.Reserve = TimeSpan.Zero;
            var blood = entities.GetComponent<BloodstreamComponent>(body);
            Assert.That(entities.System<BloodstreamSystem>().TryAddToChemicals(body,
                new Solution("GoJuice", FixedPoint2.New(units)), blood), Is.True);
            MetabolizeDrugNow(entities, body);

            var boost = entities.GetComponent<CombatStimulantComponent>(body);
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That((boost.ExpiresAt - now).TotalSeconds, Is.EqualTo(boostSeconds).Within(0.001));
            Assert.That(dependency.Reserve.TotalSeconds, Is.EqualTo(reserveSeconds).Within(0.001));
            var solutions = entities.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(body, blood.ChemicalSolutionName, out _, out var chemicals), Is.True);
            Assert.That(chemicals!.GetTotalPrototypeQuantity("GoJuice"), Is.EqualTo(FixedPoint2.Zero));

            var expiration = boost.ExpiresAt;
            var reserve = dependency.Reserve;
            MetabolizeDrugNow(entities, body);
            Assert.That(boost.ExpiresAt, Is.EqualTo(expiration), "An empty bloodstream must not count the dose twice.");
            Assert.That(dependency.Reserve, Is.EqualTo(reserve));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GoJuiceBoostsHeldWeaponsWithoutDependencyAndExpiresCleanly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, Vector2.Zero);
            var body = entities.SpawnEntity("MobHuman", coordinates);
            var weapon = entities.SpawnEntity("Crowbar", coordinates);
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var movement = entities.GetComponent<MovementSpeedModifierComponent>(body);
            var speed = movement.SprintSpeedModifier;
            var armedRate = melee.GetAttackRate(weapon, body);
            var unarmedRate = melee.GetAttackRate(body, body);
            Assert.That(entities.System<BloodstreamSystem>().TryAddToChemicals(body, new Solution("GoJuice", 5)), Is.True);
            MetabolizeDrugNow(entities, body);
            var boost = entities.GetComponent<CombatStimulantComponent>(body);
            Assert.That(entities.HasComponent<ChemicalDependencyComponent>(body), Is.False);
            Assert.That(melee.GetAttackRate(weapon, body), Is.EqualTo(armedRate * 1.3f).Within(0.001));
            Assert.That(melee.GetAttackRate(body, body), Is.EqualTo(unarmedRate * 1.3f).Within(0.001));
            Assert.That(movement.SprintSpeedModifier, Is.EqualTo(speed * 1.2f).Within(0.001));

            // A second dose extends time without multiplying the strength.
            Assert.That(entities.System<BloodstreamSystem>().TryAddToChemicals(body, new Solution("GoJuice", 5)), Is.True);
            MetabolizeDrugNow(entities, body);
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That((boost.ExpiresAt - now).TotalSeconds, Is.EqualTo(30).Within(0.001));
            Assert.That(melee.GetAttackRate(weapon, body), Is.EqualTo(armedRate * 1.3f).Within(0.001));
            boost.ExpiresAt = now;
            entities.System<CombatStimulantSystem>().Update(0);
            Assert.That(melee.GetAttackRate(weapon, body), Is.EqualTo(armedRate).Within(0.001));
            Assert.That(movement.SprintSpeedModifier, Is.EqualTo(speed).Within(0.001));
            entities.DeleteEntity(map);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChemicalWithdrawalPersistsThroughGeneTogglingAndRequiresProportionalRecovery()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobAsakim", new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            var genome = entities.GetComponent<GenomeComponent>(body);
            var dependency = entities.GetComponent<ChemicalDependencyComponent>(body);
            var needs = entities.System<ChemicalDependencySystem>();
            Assert.That(entities.System<SharedChemicalEffectsSystem>(), Is.SameAs(needs));
            var movement = entities.GetComponent<MovementSpeedModifierComponent>(body);
            var baseSpeed = movement.SprintSpeedModifier;
            var alerts = entities.System<AlertsSystem>();
            Assert.That(alerts.IsShowingAlert(body, dependency.Alert), Is.True);
            dependency.Reserve = TimeSpan.FromSeconds(-119);
            dependency.NextUpdate = TimeSpan.Zero;
            needs.Update(0);
            Assert.That(dependency.Stage, Is.EqualTo(1));
            Assert.That(dependency.MovementMultiplier, Is.EqualTo(0.8f));
            Assert.That(dependency.AttackRateMultiplier, Is.EqualTo(0.75f));

            var damage = entities.GetComponent<DamageableComponent>(body);
            var damageBefore = damage.TotalDamage;
            dependency.Reserve = TimeSpan.FromSeconds(-299);
            dependency.NextUpdate = TimeSpan.Zero;
            needs.Update(0);
            Assert.That(dependency.Stage, Is.EqualTo(2));
            Assert.That(damage.TotalDamage.Float(), Is.EqualTo(damageBefore.Float() + 0.5f).Within(0.01));
            Assert.That(needs.TrySatisfy(body, "Water", 30), Is.False);
            Assert.That(needs.TrySatisfy(body, "GoJuice", 1), Is.True);
            Assert.That(dependency.Reserve, Is.EqualTo(TimeSpan.FromMinutes(-4)));
            Assert.That(dependency.Stage, Is.EqualTo(1), "A small dose must not erase the entire withdrawal deficit.");

            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(dependency.Reserve, Is.EqualTo(TimeSpan.FromMinutes(-4)));
            var block = genetics.GetRound().Mutations.IndexOf("GeneticGoJuiceDependency");
            Assert.That(genetics.TrySetBlock((body, genome), block, 0, body), Is.True);
            Assert.That(entities.HasComponent<ChemicalDependencyComponent>(body), Is.False);
            Assert.That(movement.SprintSpeedModifier, Is.EqualTo(baseSpeed).Within(0.001));
            Assert.That(alerts.IsShowingAlert(body, dependency.Alert), Is.False);
            Assert.That(genome.Stability, Is.EqualTo(40));
            Enable(entities, body, "GeneticGoJuiceDependency");
            dependency = entities.GetComponent<ChemicalDependencyComponent>(body);
            Assert.That(dependency.Reserve, Is.EqualTo(TimeSpan.FromMinutes(-4)));
            Assert.That(dependency.Stage, Is.EqualTo(1));
            Assert.That(movement.SprintSpeedModifier, Is.EqualTo(baseSpeed * 0.8f).Within(0.001));
            Assert.That(alerts.IsShowingAlert(body, dependency.Alert), Is.True);
            Assert.That(genome.Stability, Is.EqualTo(100));
            Assert.That(needs.TrySatisfy(body, "GoJuice", 30), Is.True);
            Assert.That(dependency.Reserve, Is.EqualTo(TimeSpan.FromMinutes(26)));
            Assert.That(dependency.Stage, Is.EqualTo(-1));
            Assert.That(dependency.MovementMultiplier, Is.EqualTo(1f));
            Assert.That(needs.TrySatisfy(body, "GoJuice", 30), Is.True);
            Assert.That(dependency.Reserve, Is.EqualTo(TimeSpan.FromMinutes(30)));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    private static void MetabolizeDrugNow(IEntityManager entities, EntityUid body)
    {
        var metabolism = entities.System<MetabolizerSystem>();
        foreach (var (organ, _) in entities.System<SharedBodySystem>().GetBodyOrgans(body))
        {
            if (entities.TryGetComponent<MetabolizerComponent>(organ, out var metabolizer))
                metabolism.Metabolize((organ, metabolizer));
        }
    }
}
