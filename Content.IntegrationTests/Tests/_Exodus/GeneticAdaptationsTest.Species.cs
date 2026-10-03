#nullable enable
using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server.Abilities.Chitinid;
using Content.Server.Temperature.Components;
using Content.Shared._DV.Weapons.Ranged.Components;
using Content.Shared._Exodus.Genetics;
using Content.Shared._Exodus.Mining.Components;
using Content.Shared._Mono.Claws.Components;
using Content.Shared.Actions;
using Content.Shared.FixedPoint;
using Content.Shared.Sericulture;
using Content.Shared.StepTrigger.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [Test]
    public async Task NativeSpeciesGenomesFitTheLimitAndKeepTheirStabilityAfterStabilization()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var genetics = entities.System<GeneticsSystem>();
            var round = genetics.GetRound();
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            foreach (var mutation in prototypes.EnumeratePrototypes<GeneticMutationPrototype>())
                Assert.That(round.Mutations, Does.Contain(new ProtoId<GeneticMutationPrototype>(mutation.ID)), mutation.ID);

            var cases = new (string Prototype, int Stability)[]
            {
                ("MobHuman", 60), ("MobDwarf", 55), ("MobReptilian", 50), ("MobMoth", 40),
                ("MobArachnid", 40), ("MobDiona", 50), ("MobVox", 55), ("MobSlimePerson", 40),
                ("MobFelinid", 60), ("MobVulpkanin", 60), ("MobFeroxi", 60), ("MobChitinid", 35),
                ("MobResomi", 70), ("MobHydrakin", 60), ("MobTajaran", 55), ("MobKidan", 40),
                ("MobAsakim", 100),
            };
            foreach (var (prototype, stability) in cases)
            {
                var body = entities.SpawnEntity(prototype, new EntityCoordinates(map, Vector2.Zero));
                if (!genetics.TryGetLivingGenome(body, out var genome))
                    throw new AssertionException($"Missing living genome: {prototype}");
                Assert.That(genome.InitialMutations.Count, Is.LessThanOrEqualTo(3), prototype);
                Assert.That(genome.Active, Is.EquivalentTo(genome.InitialMutations), prototype);
                Assert.That(genome.Stability, Is.EqualTo(stability), prototype);
                Assert.That(genetics.HasGeneticModifications(body), Is.False, prototype);
                Enable(entities, body, "GeneticHearing");
                Assert.That(genetics.TryStabilize(body), Is.True, prototype);
                Assert.That(genome.Active, Is.EquivalentTo(genome.InitialMutations), prototype);
                Assert.That(genome.Stability, Is.EqualTo(stability), prototype);
                Assert.That(genetics.HasGeneticModifications(body), Is.False, prototype);
                if (genome.InitialMutations.Count > 0)
                {
                    var block = round.Mutations.IndexOf(genome.InitialMutations[0]);
                    Assert.That(genetics.TrySetBlock((body, genome), block, 0, body), Is.True, prototype);
                    Assert.That(genetics.HasGeneticModifications(body), Is.True, prototype);
                    Assert.That(genetics.TryStabilize(body), Is.True, prototype);
                    Assert.That(genome.Blocks[block], Is.Zero, prototype);
                    Assert.That(genetics.HasGeneticModifications(body), Is.True, prototype);
                }
                entities.DeleteEntity(body);
            }
            entities.DeleteEntity(map);
            DeleteCipher(entities, round.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase("MobHuman")]
    [TestCase("MobResomi")]
    public async Task TemperatureGenesRestoreBaselineAndRejectConflictingSamplesAtomically(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity(prototype, new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            var temperature = entities.GetComponent<TemperatureComponent>(body);
            var originalThreshold = temperature.ColdDamageThreshold;
            var originalDamage = temperature.ColdDamage.DamageDict["Cold"];
            var originalTemperature = temperature.CurrentTemperature;
            var genome = Enable(entities, body, "GeneticCryostasis");
            var round = genetics.GetRound();
            Assert.That(temperature.ColdDamageThreshold, Is.EqualTo(originalThreshold - 250));
            Assert.That(temperature.ColdDamage.DamageDict["Cold"], Is.EqualTo(originalDamage * 0.1f));
            Enable(entities, body, "GeneticQuietStep");
            Assert.That(temperature.ColdDamageThreshold, Is.EqualTo(originalThreshold - 250), "Reconciliation must not compound a thermal modifier.");

            Enable(entities, body, "GeneticWoodenArmor");
            var revision = genome.Revision;
            var before = genetics.Capture((body, genome));
            var conflicting = round.Mutations.IndexOf("GeneticChitinArmor");
            Assert.That(genetics.TrySetBlock((body, genome), conflicting, GeneticsSystem.MaxBlockValue, body), Is.False);
            Assert.That(genome.Revision, Is.EqualTo(revision));
            var sample = genetics.Capture((body, genome));
            sample.Blocks[conflicting] = GeneticsSystem.MaxBlockValue;
            Assert.That(genetics.TryApply((body, genome), sample, body), Is.False);
            Assert.That(genome.Blocks, Is.EqualTo(before.Blocks));

            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(temperature.ColdDamageThreshold, Is.EqualTo(originalThreshold));
            Assert.That(temperature.ColdDamage.DamageDict["Cold"], Is.EqualTo(originalDamage));
            Assert.That(temperature.CurrentTemperature, Is.EqualTo(originalTemperature), "Switching an adaptation must not heat or cool the patient.");
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ClawProgressSurvivesTogglingAndWeaponSettingsAreRestored()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var melee = entities.GetComponent<MeleeWeaponComponent>(body);
            var wide = melee.CanWideSwing;
            var disarm = melee.AltDisarm;
            var genome = Enable(entities, body, "GeneticGrowingClaws");
            var claws = entities.GetComponent<ClawsComponent>(body);
            claws.ClawStage = "ReptilianHugeClaws";
            claws.GrowTimer = TimeSpan.FromSeconds(123);
            entities.System<Content.Server._Mono.Claws.ClawsSystem>().UpdateClaws(body, claws);
            var genetics = entities.System<GeneticsSystem>();
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(entities.HasComponent<ClawsComponent>(body), Is.False);
            Assert.That(entities.HasComponent<PlayerAccuracyModifierComponent>(body), Is.False);
            Assert.That(melee.CanWideSwing, Is.EqualTo(wide));
            Assert.That(melee.AltDisarm, Is.EqualTo(disarm));
            Enable(entities, body, "GeneticGrowingClaws");
            claws = entities.GetComponent<ClawsComponent>(body);
            Assert.That(claws.ClawStage.Id, Is.EqualTo("ReptilianHugeClaws"));
            Assert.That(claws.GrowTimer, Is.EqualTo(TimeSpan.FromSeconds(123)));
            claws.ClawStage = "ReptilianDeclawed";
            Assert.That(genetics.TryStabilize(body), Is.True);
            Enable(entities, body, "GeneticGrowingClaws");
            Assert.That(entities.GetComponent<ClawsComponent>(body).ClawStage.Id, Is.EqualTo("ReptilianDeclawed"));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RadiationMetabolismKeepsStoredRadiationWithoutGrantingFreeChitzite()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobChitinid", new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            var genome = entities.GetComponent<GenomeComponent>(body);
            var metabolism = entities.GetComponent<ChitinidComponent>(body);
            metabolism.AmountAbsorbed = FixedPoint2.New(12);
            var originalAction = metabolism.ChitziteAction;
            var block = genetics.GetRound().Mutations.IndexOf("GeneticRadiotrophicMetabolism");
            Assert.That(genetics.TrySetBlock((body, genome), block, 0, body), Is.True);
            Assert.That(entities.HasComponent<ChitinidComponent>(body), Is.False);
            Enable(entities, body, "GeneticRadiotrophicMetabolism");
            metabolism = entities.GetComponent<ChitinidComponent>(body);
            Assert.That(metabolism.AmountAbsorbed, Is.EqualTo(FixedPoint2.New(12)));
            Assert.That(metabolism.ChitziteAction, Is.Not.EqualTo(originalAction));
            var actions = entities.System<SharedActionsSystem>();
            Assert.That(actions.GetCharges(metabolism.ChitziteAction), Is.Zero);
            Assert.That(actions.TryGetActionData(metabolism.ChitziteAction, out var action), Is.True);
            Assert.That(action!.Enabled, Is.False);
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TransferredSpeciesAbilitiesDisappearWithTheirGene()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genome = Enable(entities, body, "GeneticOreSense");
            Assert.That(entities.GetComponent<MiningScannerUserComponent>(body).QueueRemoval, Is.False);
            Enable(entities, body, "GeneticQuietStep");
            Enable(entities, body, "GeneticWeb");
            Assert.That(entities.HasComponent<NoShoesSilentFootstepsComponent>(body), Is.True);
            Assert.That(entities.HasComponent<SericultureComponent>(body), Is.True);
            var genetics = entities.System<GeneticsSystem>();
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(entities.HasComponent<MiningScannerComponent>(body), Is.False);
            Assert.That(entities.GetComponent<MiningScannerUserComponent>(body).QueueRemoval, Is.True);
            Assert.That(entities.HasComponent<NoShoesSilentFootstepsComponent>(body), Is.False);
            Assert.That(entities.HasComponent<SericultureComponent>(body), Is.False);
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
