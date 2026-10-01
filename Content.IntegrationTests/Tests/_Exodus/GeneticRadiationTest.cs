using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server.Radiation.Systems;
using Content.Shared._Exodus.Radiation;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(GeneticsSystem))]
public sealed class GeneticRadiationTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: geneticMutation
          id: GeneticsTestRadiationExcluded
          name: genetics-gene-breathing
          description: genetics-gene-breathing-desc
          activationMessage: genetics-feeling-breathing
          radiationWeight: 0

        - type: damageModifierSet
          id: GeneticsTestRadiationImmune
          coefficients:
            Radiation: 0

        - type: damageModifierSet
          id: GeneticsTestRadiationResistant
          coefficients:
            Radiation: 0.5
        """;

    [Test]
    public async Task RadiationActivatesUnscannedGenomeAndRespectsWeightsAndCooldown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            var radiation = entities.System<RadiationSystem>();
            var round = genetics.GetRound();
            Assert.That(round.RadiationMutationCooldown, Is.EqualTo(TimeSpan.FromSeconds(60)));
            // Make the chance deterministic while exercising the real irradiation/damage event chain.
            round.RadiationMutationRate = 1000;
            for (var i = 0; i < round.Mutations.Count; i++)
                round.Mutations[i] = null;
            round.Mutations[0] = "GeneticNoBreathing";
            round.Mutations[1] = "GeneticClotting";
            round.Mutations[2] = "GeneticsTestRadiationExcluded";

            Assert.That(entities.HasComponent<GenomeComponent>(body), Is.False);
            radiation.IrradiateEntity(body, 0.1f, 1f);
            var genome = entities.GetComponent<GenomeComponent>(body);
            Assert.That(genome.Active.Count, Is.EqualTo(1));
            Assert.That(genome.Active.Contains("GeneticsTestRadiationExcluded"), Is.False);
            Assert.That(genome.Stability, Is.EqualTo(genome.StabilityCapacity - 15));
            var changed = 0;
            for (var i = 0; i < genome.Blocks.Count; i++)
            {
                if (genome.Blocks[i] == genome.Baseline[i])
                    continue;
                changed++;
                Assert.That(i, Is.LessThan(2), "Only an eligible, nonempty block may change.");
                Assert.That(GeneticsSystem.IsBlockActive(genome.Blocks[i], round.Thresholds[i]), Is.True);
                Assert.That(GeneticsSystem.IsBlockActive(genome.Baseline[i], round.Thresholds[i]), Is.False);
            }
            Assert.That(changed, Is.EqualTo(1));
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(genome.NextRadiationMutation, Is.EqualTo(now + round.RadiationMutationCooldown));
            var revision = genome.Revision;
            var sample = genetics.Capture((body, genome));

            radiation.IrradiateEntity(body, 0.1f, 1f);
            Assert.That(genome.Revision, Is.EqualTo(revision), "Exposure during cooldown cannot change the genome.");
            Assert.That(genome.Blocks, Is.EqualTo(sample.Blocks));

            genome.NextRadiationMutation = now;
            genome.Stability = -20;
            genome.NextUpdate = now;
            genetics.Update(0);
            Assert.That(genome.Revision, Is.EqualTo(revision), "Instability damage must not trigger further mutations.");

            radiation.IrradiateEntity(body, 0.1f, 1f);
            Assert.That(genome.Active.Count, Is.EqualTo(2), "The next success must choose the remaining inactive gene.");
            Assert.That(genome.Active.Contains("GeneticNoBreathing"), Is.True);
            Assert.That(genome.Active.Contains("GeneticClotting"), Is.True);
            Assert.That(genome.Blocks[2], Is.EqualTo(genome.Baseline[2]));
            var secondRevision = genome.Revision;
            genome.NextRadiationMutation = now;
            radiation.IrradiateEntity(body, 0.1f, 1f);
            Assert.That(genome.Revision, Is.EqualTo(secondRevision), "No eligible genes means no genome edit.");
            Assert.That(genome.NextRadiationMutation, Is.EqualTo(now), "An unsuccessful mutation does not start a cooldown.");

            genome.NextRadiationMutation = now + round.RadiationMutationCooldown;
            Assert.That(genetics.TryStabilize(body), Is.True);
            radiation.IrradiateEntity(body, 0.1f, 1f);
            Assert.That(genome.Active, Is.Empty, "Chemical reset cannot bypass the radiation cooldown.");
            entities.DeleteEntity(map);
            DeleteCipher(entities, round.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase("GeneticsTestRadiationImmune", 0f)]
    [TestCase("GeneticsTestRadiationResistant", 0.1f)]
    public async Task RadiationReactionUsesAppliedDamageAndIgnoresOtherDamage(string modifier, float expected)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            var round = genetics.GetRound();
            round.RadiationMutationRate = 0;
            var probe = entities.AddComponent<GeneticRadiationTestReceiverComponent>(body);
            var buff = entities.AddComponent<DamageProtectionBuffComponent>(body);
            buff.Modifiers.Add(modifier, server.ProtoMan.Index<DamageModifierSetPrototype>(modifier));
            entities.System<RadiationSystem>().IrradiateEntity(body, 0.2f, 1f);
            Assert.That(probe.Damage.Float(), Is.EqualTo(expected).Within(0.001f));
            Assert.That(probe.Count, Is.EqualTo(expected > 0 ? 1 : 0));
            Assert.That(entities.HasComponent<GenomeComponent>(body), Is.False,
                "Disabled radiation mutations must not initialize an unscanned genome.");

            var damage = entities.System<DamageableSystem>();
            entities.RemoveComponent<DamageProtectionBuffComponent>(body);
            damage.TryChangeDamage(body, new DamageSpecifier { DamageDict = new() { ["Radiation"] = 1 } });
            damage.TryChangeDamage(body, new DamageSpecifier { DamageDict = new() { ["Radiation"] = -1 } });
            damage.TryChangeDamage(body, new DamageSpecifier { DamageDict = new() { ["Poison"] = 1 } });
            Assert.That(probe.Damage.Float(), Is.EqualTo(expected).Within(0.001f),
                "Direct radiation damage, healing and other damage must not emit irradiation reactions.");
            Assert.That(probe.Count, Is.EqualTo(expected > 0 ? 1 : 0));

            round.RadiationMutationRate = 1000;
            buff = entities.AddComponent<DamageProtectionBuffComponent>(body);
            buff.Modifiers.Add("GeneticsTestRadiationImmune",
                server.ProtoMan.Index<DamageModifierSetPrototype>("GeneticsTestRadiationImmune"));
            entities.System<RadiationSystem>().IrradiateEntity(body, 1f, 1f);
            Assert.That(entities.HasComponent<GenomeComponent>(body), Is.False,
                "Fully blocked irradiation cannot activate a mutation, even with a guaranteed chance.");
            entities.DeleteEntity(map);
            DeleteCipher(entities, round.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase("dead")]
    [TestCase("incompatible")]
    [TestCase("critical")]
    public async Task RadiationRequiresCompatibleLivingBody(string state)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            var round = genetics.GetRound();
            round.RadiationMutationRate = 1000;
            for (var i = 0; i < round.Mutations.Count; i++)
                round.Mutations[i] = null;
            round.Mutations[0] = "GeneticNoBreathing";
            if (state == "incompatible")
                entities.AddComponent<GeneticIncompatibleComponent>(body);
            else
                entities.System<MobStateSystem>().ChangeMobState(body, state == "dead" ? MobState.Dead : MobState.Critical);

            var ev = new RadiationDamageReceivedEvent(FixedPoint2.New(0.1));
            entities.EventBus.RaiseLocalEvent(body, ref ev);
            Assert.That(entities.HasComponent<GenomeComponent>(body), Is.EqualTo(state == "critical"));
            if (state == "critical")
                Assert.That(entities.GetComponent<GenomeComponent>(body).Active.Contains("GeneticNoBreathing"), Is.True);
            entities.DeleteEntity(map);
            DeleteCipher(entities, round.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    private static void DeleteCipher(IEntityManager entities, string context)
    {
        var query = entities.AllEntityQueryEnumerator<GeneticsRoundComponent>();
        while (query.MoveNext(out var uid, out var round))
        {
            if (round.Context == context)
                entities.QueueDeleteEntity(uid);
        }
    }
}

[RegisterComponent]
public sealed partial class GeneticRadiationTestReceiverComponent : Component
{
    public FixedPoint2 Damage;
    public int Count;
}

public sealed class GeneticRadiationTestReceiverSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticRadiationTestReceiverComponent, RadiationDamageReceivedEvent>(OnReceived);
    }

    private void OnReceived(Entity<GeneticRadiationTestReceiverComponent> ent, ref RadiationDamageReceivedEvent args)
    {
        ent.Comp.Damage += args.Damage;
        ent.Comp.Count++;
    }
}
