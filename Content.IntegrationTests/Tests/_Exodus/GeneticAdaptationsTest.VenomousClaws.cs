#nullable enable
using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Chemistry.Components;
using Content.Server.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [TestCase("MobAsakim")]
    [TestCase("MobAsakimRandom")]
    [TestCase("MobAsakimGhostrole")]
    [TestCase("MobCenturionAsakimGhostrole")]
    [TestCase("MobCenturionAsakimGhostroleNoTimelock")]
    [TestCase("MobPrefectAsakimGhostrole")]
    [TestCase("MobPrefectAsakimGhostroleNoTimelock")]
    public async Task AsakimVenomousClawsAreNativeAndCanBeDisabled(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, Vector2.Zero);
            var body = entities.SpawnEntity(prototype, coordinates);
            var target = entities.SpawnEntity("MobHuman", coordinates);
            var genetics = entities.System<GeneticsSystem>();
            var genome = entities.GetComponent<GenomeComponent>(body);
            Assert.That(genome.Active.Contains("GeneticVenomousClaws"), Is.True);
            Assert.That(genome.Stability, Is.EqualTo(85));

            Enable(entities, body, "GeneticQuietStep");
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(genome.Active.Contains("GeneticVenomousClaws"), Is.True);
            Assert.That(genome.Stability, Is.EqualTo(85));
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var multiplier = entities.System<DamageableSystem>().UniversalMeleeDamageModifier;
            var damage = melee.GetDamage(body, body);
            Assert.That(damage.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(15) * multiplier));
            Assert.That(damage.DamageDict["Structural"], Is.EqualTo(FixedPoint2.New(24) * multiplier));

            var blood = entities.GetComponent<BloodstreamComponent>(target);
            var solutions = entities.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(target, blood.ChemicalSolutionName, out _, out var chemicals), Is.True);
            var hit = new MeleeHitEvent([target], body, body, damage, null);
            entities.EventBus.RaiseLocalEvent(body, hit);
            Assert.That(chemicals!.GetTotalPrototypeQuantity("GastroToxin"), Is.EqualTo(FixedPoint2.New(1)));

            var block = genetics.GetRound().Mutations.IndexOf("GeneticVenomousClaws");
            Assert.That(genetics.TrySetBlock((body, genome), block, 0, body), Is.True);
            Assert.That(genome.Stability, Is.EqualTo(100));
            Assert.That(entities.HasComponent<MeleeChemicalInjectorComponent>(body), Is.False);
            Assert.That(entities.HasComponent<SolutionRegenerationComponent>(body), Is.False);
            Assert.That(melee.GetDamage(body, body).DamageDict.ContainsKey("Slash"), Is.False);
            hit = new MeleeHitEvent([target], body, body, melee.GetDamage(body, body), null);
            entities.EventBus.RaiseLocalEvent(body, hit);
            Assert.That(chemicals.GetTotalPrototypeQuantity("GastroToxin"), Is.EqualTo(FixedPoint2.New(1)));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AcquiredVenomousClawsPreserveBodySolutionsAndRegenerateOnlyWhileActive()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, Vector2.Zero);
            var body = entities.SpawnEntity("MobHuman", coordinates);
            var target = entities.SpawnEntity("MobHuman", coordinates);
            var weapon = entities.SpawnEntity("Crowbar", coordinates);
            var solutions = entities.System<SharedSolutionContainerSystem>();
            var blood = entities.GetComponent<BloodstreamComponent>(body);
            var manager = entities.GetComponent<SolutionContainerManagerComponent>(body);
            Assert.That(entities.System<BloodstreamSystem>().TryAddToChemicals(body, new Solution("Water", 5)), Is.True);
            Assert.That(solutions.TryGetSolution(body, blood.BloodSolutionName, out var bloodEntity, out var bloodSolution), Is.True);
            Assert.That(solutions.TryGetSolution(body, blood.ChemicalSolutionName, out var chemicalsEntity, out var chemicals), Is.True);
            var bloodVolume = bloodSolution!.Volume;
            var melee = entities.System<SharedMeleeWeaponSystem>();
            var originalDamage = melee.GetDamage(body, body);
            var weaponDamage = melee.GetDamage(weapon, body);
            var genome = Enable(entities, body, "GeneticVenomousClaws");
            Assert.That(genome.Stability, Is.EqualTo(45));
            Assert.That(entities.GetComponent<SolutionContainerManagerComponent>(body), Is.SameAs(manager));
            Assert.That(solutions.TryGetSolution(body, blood.BloodSolutionName, out var currentBlood), Is.True);
            Assert.That(currentBlood, Is.EqualTo(bloodEntity));
            Assert.That(solutions.TryGetSolution(body, blood.ChemicalSolutionName, out var currentChemicals), Is.True);
            Assert.That(currentChemicals, Is.EqualTo(chemicalsEntity));
            Assert.That(bloodSolution.Volume, Is.EqualTo(bloodVolume));
            Assert.That(chemicals!.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(melee.GetDamage(weapon, body), Is.EqualTo(weaponDamage));

            var injector = entities.GetComponent<MeleeChemicalInjectorComponent>(body);
            Assert.That(solutions.TryGetSolution(body, injector.Solution, out _, out var venom), Is.True);
            Assert.That(venom!.Volume, Is.EqualTo(FixedPoint2.New(20)));
            var targetBlood = entities.GetComponent<BloodstreamComponent>(target);
            Assert.That(solutions.TryGetSolution(target, targetBlood.ChemicalSolutionName, out _, out var targetChemicals), Is.True);
            var hit = new MeleeHitEvent([target], body, body, melee.GetDamage(body, body), null) { IsHit = false };
            entities.EventBus.RaiseLocalEvent(body, hit);
            hit = new MeleeHitEvent([target], body, weapon, weaponDamage, null);
            entities.EventBus.RaiseLocalEvent(weapon, hit);
            Assert.That(targetChemicals!.GetTotalPrototypeQuantity("GastroToxin"), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(venom.Volume, Is.EqualTo(FixedPoint2.New(20)));

            hit = new MeleeHitEvent([target], body, body, melee.GetDamage(body, body), null);
            entities.EventBus.RaiseLocalEvent(body, hit);
            Assert.That(targetChemicals.GetTotalPrototypeQuantity("GastroToxin"), Is.EqualTo(FixedPoint2.New(1)));
            Assert.That(venom.Volume, Is.EqualTo(FixedPoint2.New(19)));
            var genetics = entities.System<GeneticsSystem>();
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(entities.HasComponent<MeleeChemicalInjectorComponent>(body), Is.False);
            Assert.That(entities.HasComponent<SolutionRegenerationComponent>(body), Is.False);
            Assert.That(melee.GetDamage(body, body), Is.EqualTo(originalDamage));
            entities.System<SolutionRegenerationSystem>().Update(0);
            Assert.That(venom.Volume, Is.EqualTo(FixedPoint2.New(19)));

            Enable(entities, body, "GeneticVenomousClaws");
            Assert.That(venom.Volume, Is.EqualTo(FixedPoint2.New(19)), "Toggling the gene must not refill its reservoir.");
            entities.System<SolutionRegenerationSystem>().Update(0);
            Assert.That(venom.Volume, Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(bloodSolution.Volume, Is.EqualTo(bloodVolume));
            Assert.That(chemicals.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(5)));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
