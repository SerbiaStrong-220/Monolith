using System.Linq;
using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.Cloning;
using Content.Shared._Exodus.Genetics;
using Content.Shared._Exodus.Actions;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [Test]
    public async Task SlimeTransformationPreservesAnatomyInjuriesAndCooldown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var bodies = entities.System<BodySystem>();
            var genetics = entities.System<GeneticsSystem>();
            var liver = bodies.GetBodyOrgans(body).Single(organ => organ.Component.SlotId == "liver").Id;
            Assert.That(bodies.RemoveOrgan(liver), Is.True);
            var organs = bodies.GetBodyOrgans(body).Select(organ => organ.Id).ToArray();
            var parts = bodies.GetBodyChildren(body).Select(part => part.Id).ToArray();
            var lung = organs.Single(organ => entities.HasComponent<LungComponent>(organ));
            var metabolism = entities.GetComponent<MetabolizerComponent>(lung);
            var originalTypes = metabolism.MetabolizerTypes?.ToArray();
            var blood = entities.System<BloodstreamSystem>();
            Assert.That(blood.TryModifyBloodLevel(body, -30), Is.True);
            var bloodLevel = blood.GetBloodLevelPercentage(body);
            var bloodReagent = entities.GetComponent<BloodstreamComponent>(body).BloodReagent;
            entities.System<DamageableSystem>().TryChangeDamage(body,
                new DamageSpecifier { DamageDict = { ["Poison"] = FixedPoint2.New(10) } }, ignoreResistances: true);
            var damage = entities.GetComponent<DamageableComponent>(body).TotalDamage;
            var appearance = entities.GetComponent<HumanoidAppearanceComponent>(body);
            var skinColor = appearance.SkinColor;
            var genome = Enable(entities, body, "GeneticSlime");
            var block = genetics.GetRound().Mutations.IndexOf("GeneticSlime");
            Assert.That(appearance.Species.Id, Is.EqualTo("SlimePerson"));
            Assert.That(metabolism.MetabolizerTypes!.Select(type => type.Id), Is.EquivalentTo(new[] { "Slime" }));
            Assert.That(entities.GetComponent<LungComponent>(lung).Alert.Id, Is.EqualTo("LowNitrogen"));

            var item = entities.SpawnEntity("Crowbar", new EntityCoordinates(map, Vector2.Zero));
            var hands = entities.System<SharedHandsSystem>();
            Assert.That(hands.TryPickup(body, item), Is.True);
            Assert.That(Transform(entities, body), Is.True);
            Assert.That(appearance.Species.Id, Is.EqualTo("GeneticSlimeForm"));
            Assert.That(hands.TryGetActiveItem(body, out _), Is.False);
            Assert.That(hands.TryPickup(body, item), Is.False);
            var state = entities.GetComponent<GeneticAbilityStateComponent>(body);
            var available = state.TransformationAvailable;
            Assert.That(available - server.ResolveDependency<IGameTiming>().CurTime, Is.EqualTo(TimeSpan.FromSeconds(120)));
            var transformAction = Action(entities, genome, "ActionGeneticTransform");
            Assert.That(entities.GetComponent<ActionCooldownDisplayComponent>(transformAction).End, Is.EqualTo(available));
            Assert.That(transformAction.Comp.Cooldown?.End ?? TimeSpan.Zero, Is.LessThanOrEqualTo(server.ResolveDependency<IGameTiming>().CurTime),
                "Showing the entry cooldown must not lock the return action.");
            Assert.That(Transform(entities, body), Is.True, "Returning must work during the entry cooldown.");
            Assert.That(transformAction.Comp.Cooldown?.End, Is.EqualTo(available),
                "The action system must retain the remaining cooldown after completing the return action.");
            Assert.That(Transform(entities, body), Is.False);

            // Harvesting after adaptation must not create a replacement or leave a permanently converted organ.
            Assert.That(bodies.RemoveOrgan(lung), Is.True);
            Assert.That(metabolism.MetabolizerTypes, Is.EquivalentTo(originalTypes!));
            Assert.That(entities.GetComponent<LungComponent>(lung).Alert.Id, Is.EqualTo("LowOxygen"));
            Assert.That(genetics.TrySetBlock((body, genome), block, 0, body), Is.True);
            Assert.That(appearance.Species.Id, Is.EqualTo("Human"));
            Assert.That(appearance.SkinColor, Is.EqualTo(skinColor));
            Enable(entities, body, "GeneticSlime");
            Assert.That(state.TransformationAvailable, Is.EqualTo(available));
            Assert.That(Transform(entities, body), Is.False, "Toggling the gene must not reset its cooldown.");
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(appearance.Species.Id, Is.EqualTo("Human"), "An acquired slime gene is not native to a human body.");
            Assert.That(bodies.GetBodyOrgans(body).Select(organ => organ.Id), Is.EquivalentTo(organs.Where(organ => organ != lung)));
            Assert.That(bodies.GetBodyChildren(body).Select(part => part.Id), Is.EquivalentTo(parts));
            Assert.That(entities.EntityExists(liver), Is.True);
            Assert.That(entities.EntityExists(lung), Is.True);
            Assert.That(entities.GetComponent<DamageableComponent>(body).TotalDamage, Is.EqualTo(damage));
            Assert.That(blood.GetBloodLevelPercentage(body), Is.EqualTo(bloodLevel));
            Assert.That(entities.GetComponent<BloodstreamComponent>(body).BloodReagent, Is.EqualTo(bloodReagent));
            var animal = entities.SpawnEntity("MobAdultSlimesBlue", new EntityCoordinates(map, Vector2.One));
            Assert.That(genetics.TryGetGenome(animal, out _), Is.False);
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NativeSlimeKeepsAppearanceAndReturnsOnDeathOrGeneRemoval(bool removeGene)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobSlimePerson", new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            Assert.That(genetics.TryGetGenome(body, out var genome), Is.True);
            Assert.That(genome!.Active.Select(id => id.Id), Does.Contain("GeneticSlime"));
            Assert.That(genome.Actions.ContainsKey("ActionGeneticTransform"), Is.True);
            var appearance = entities.GetComponent<HumanoidAppearanceComponent>(body);
            var color = Color.FromHex("#ce76af");
            entities.System<SharedHumanoidAppearanceSystem>().SetSkinColor(body, color);
            var markings = appearance.MarkingSet;
            var organs = entities.System<BodySystem>().GetBodyOrgans(body).Select(organ => organ.Id).ToArray();
            genetics.Reconcile((body, genome));
            Assert.That(appearance.SkinColor, Is.EqualTo(color));
            Assert.That(appearance.MarkingSet, Is.SameAs(markings), "Native appearance must not be regenerated.");
            entities.System<DamageableSystem>().TryChangeDamage(body,
                new DamageSpecifier { DamageDict = { ["Poison"] = FixedPoint2.New(15) } }, ignoreResistances: true);
            var damage = entities.GetComponent<DamageableComponent>(body).TotalDamage;
            Assert.That(Transform(entities, body), Is.True);
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(entities.GetComponent<GeneticEffectsComponent>(body).InAlternateForm, Is.True,
                "Genostabilin must preserve a native slime's transformation.");
            if (removeGene)
            {
                var block = genetics.GetRound().Mutations.IndexOf("GeneticSlime");
                Assert.That(genetics.TrySetBlock((body, genome), block, 0, body), Is.True);
            }
            else
                entities.System<MobStateSystem>().ChangeMobState(body, MobState.Dead);
            Assert.That(appearance.Species.Id, Is.EqualTo("SlimePerson"));
            Assert.That(appearance.SkinColor, Is.EqualTo(color));
            Assert.That(entities.GetComponent<GeneticEffectsComponent>(body).InAlternateForm, Is.False);
            Assert.That(entities.GetComponent<DamageableComponent>(body).TotalDamage, Is.EqualTo(damage));
            Assert.That(entities.System<BodySystem>().GetBodyOrgans(body).Select(organ => organ.Id), Is.EquivalentTo(organs));
            if (!removeGene)
                Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.True);
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SlimeMimicryAndCloningKeepIndependentOriginalAppearances(bool mimicFirst)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var model = entities.SpawnEntity("MobReptilian", new EntityCoordinates(map, new Vector2(0.5f, 0)));
            var genetics = entities.System<GeneticsSystem>();
            var genome = Enable(entities, body, "GeneticMimic");
            if (!mimicFirst)
                Enable(entities, body, "GeneticSlime");
            var mimic = new GeneticMimicEvent { Performer = body, Target = model };
            entities.EventBus.RaiseLocalEvent(body, mimic);
            Assert.That(mimic.Handled, Is.True);
            if (mimicFirst)
                Enable(entities, body, "GeneticSlime");
            Assert.That(entities.GetComponent<HumanoidAppearanceComponent>(body).Species.Id, Is.EqualTo("Reptilian"));
            Assert.That(Transform(entities, body), Is.True);

            var clone = entities.System<CloningSystem>().SpawnClone(new EntityCoordinates(map, Vector2.One), null, sourceBody: body);
            Assert.That(entities.GetComponent<MetaDataComponent>(clone).EntityPrototype!.ID, Is.EqualTo("MobHuman"));
            var cloneAppearance = entities.GetComponent<HumanoidAppearanceComponent>(clone);
            Assert.That(cloneAppearance.Species.Id, Is.EqualTo("Reptilian"));
            var cloneState = entities.GetComponent<GeneticAbilityStateComponent>(clone);
            var sourceState = entities.GetComponent<GeneticAbilityStateComponent>(body);
            Assert.That(cloneState.OriginalAppearance?.Species.Id, Is.EqualTo("SlimePerson"));
            Assert.That(cloneState.GeneticOriginalAppearance?.Species.Id, Is.EqualTo("Human"));
            Assert.That(cloneState.OriginalAppearance, Is.Not.SameAs(sourceState.OriginalAppearance));
            Assert.That(cloneState.GeneticOriginalAppearance, Is.Not.SameAs(sourceState.GeneticOriginalAppearance));
            Assert.That(cloneState.TransformationColor, Is.EqualTo(sourceState.TransformationColor));
            Assert.That(cloneState.TransformationAvailable, Is.EqualTo(sourceState.TransformationAvailable));
            Assert.That(entities.GetComponent<GeneticEffectsComponent>(clone).InAlternateForm, Is.False);
            Assert.That(genetics.TryGetGenome(clone, out var cloneGenome), Is.True);
            var round = genetics.GetRound();
            var mimicBlock = round.Mutations.IndexOf("GeneticMimic");
            var slimeBlock = round.Mutations.IndexOf("GeneticSlime");
            Assert.That(genetics.TrySetBlock((clone, cloneGenome!), mimicBlock, 0, clone), Is.True);
            Assert.That(cloneAppearance.Species.Id, Is.EqualTo("SlimePerson"));
            Assert.That(genetics.TrySetBlock((clone, cloneGenome!), slimeBlock, 0, clone), Is.True);
            Assert.That(cloneAppearance.Species.Id, Is.EqualTo("Human"));
            Assert.That(entities.GetComponent<GeneticEffectsComponent>(body).InAlternateForm, Is.True);
            Assert.That(genetics.TrySetBlock((body, genome), slimeBlock, 0, body), Is.True);
            Assert.That(entities.GetComponent<HumanoidAppearanceComponent>(body).Species.Id, Is.EqualTo("Reptilian"));
            Assert.That(genetics.TrySetBlock((body, genome), mimicBlock, 0, body), Is.True);
            Assert.That(entities.GetComponent<HumanoidAppearanceComponent>(body).Species.Id, Is.EqualTo("Human"));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CriticalSlimeReturnHealsWithoutRegrowingOrgansOrRevivingTheDead(bool diesBeforeHealing)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid map = default;
        EntityUid body = default;
        EntityUid[] organs = [];
        string context = default!;
        TimeSpan available = default;
        FixedPoint2 damageBeforeReturn = default;
        await server.WaitAssertion(() =>
        {
            map = entities.System<SharedMapSystem>().CreateMap();
            body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var bodies = entities.System<BodySystem>();
            var liver = bodies.GetBodyOrgans(body).Single(organ => organ.Component.SlotId == "liver").Id;
            Assert.That(bodies.RemoveOrgan(liver), Is.True);
            organs = bodies.GetBodyOrgans(body).Select(organ => organ.Id).ToArray();
            var genome = Enable(entities, body, "GeneticSlime");
            context = genome.Context;
            entities.EnsureComponent<GodmodeComponent>(body); // Prevent the empty test map's atmosphere from affecting assertions.
            Assert.That(Transform(entities, body), Is.True);
            available = entities.GetComponent<GeneticAbilityStateComponent>(body).TransformationAvailable;
            var states = entities.System<MobStateSystem>();
            var criticalDamage = entities.GetComponent<MobThresholdsComponent>(body).Thresholds.First(threshold => threshold.Value == MobState.Critical).Key;
            var damage = entities.GetComponent<DamageableComponent>(body);
            entities.System<DamageableSystem>().SetDamage(body, damage,
                new DamageSpecifier { DamageDict = { ["Poison"] = criticalDamage } });
            damageBeforeReturn = damage.TotalDamage;
            Assert.That(entities.GetComponent<MobStateComponent>(body).CurrentState, Is.EqualTo(MobState.Critical));
            Assert.That(entities.GetComponent<GeneticEffectsComponent>(body).InAlternateForm, Is.False);
            Assert.That(entities.GetComponent<HumanoidAppearanceComponent>(body).Species.Id, Is.EqualTo("SlimePerson"));
            if (diesBeforeHealing)
                states.ChangeMobState(body, MobState.Dead);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<DamageableComponent>(body).TotalDamage,
                Is.EqualTo(diesBeforeHealing ? damageBeforeReturn : FixedPoint2.Zero));
            Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.EqualTo(diesBeforeHealing));
            Assert.That(entities.System<MobStateSystem>().IsAlive(body), Is.EqualTo(!diesBeforeHealing));
            Assert.That(entities.GetComponent<GeneticAbilityStateComponent>(body).TransformationAvailable, Is.EqualTo(available));
            Assert.That(entities.System<BodySystem>().GetBodyOrgans(body).Select(organ => organ.Id), Is.EquivalentTo(organs));
            entities.DeleteEntity(map);
            DeleteCipher(entities, context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GeneticCooldownIsDisplayedAndSurvivesActionReplacement()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genome = Enable(entities, body, "GeneticHearing");
            var action = Action(entities, genome, "ActionGeneticHearing");
            var ev = new GeneticHearingEvent();
            entities.System<SharedActionsSystem>().PerformAction(body, entities.GetComponent<ActionsComponent>(body),
                action.Owner, action.Comp, ev, server.ResolveDependency<IGameTiming>().CurTime);
            Assert.That(ev.Handled, Is.True);
            var available = entities.GetComponent<GeneticAbilityStateComponent>(body).HearingAvailable;
            Assert.That(action.Comp.Cooldown?.End, Is.EqualTo(available));
            Assert.That(entities.GetComponent<ActionCooldownDisplayComponent>(action).End, Is.EqualTo(available));
            var genetics = entities.System<GeneticsSystem>();
            Assert.That(genetics.TryStabilize(body), Is.True);
            Enable(entities, body, "GeneticHearing");
            var replacement = Action(entities, genome, "ActionGeneticHearing");
            Assert.That(replacement.Owner, Is.Not.EqualTo(action.Owner));
            Assert.That(replacement.Comp.Cooldown?.End, Is.EqualTo(available));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    private static bool Transform(IEntityManager entities, EntityUid body)
    {
        var action = Action(entities, entities.GetComponent<GenomeComponent>(body), "ActionGeneticTransform");
        var ev = new GeneticTransformEvent();
        entities.System<SharedActionsSystem>().PerformAction(body, entities.GetComponent<ActionsComponent>(body),
            action.Owner, action.Comp, ev, IoCManager.Resolve<IGameTiming>().CurTime);
        return ev.Handled;
    }
}
