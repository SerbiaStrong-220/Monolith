using System.Linq;
using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Shared._Exodus.Genetics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [TestCase("MobAsakim", "GeneticJump")]
    [TestCase("MobAsakimRandom", "GeneticJump")]
    [TestCase("MobAsakim", "GeneticVenomousClaws")]
    [TestCase("MobMoth", "GeneticCocoon")]
    [TestCase("MobArachnid", "GeneticWeb")]
    [TestCase("MobSlimePerson", "GeneticSlime")]
    public async Task GenostabilinPreservesNativeBlocksAndRemovesAcquiredMutations(string prototype, string nativeMutation)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity(prototype, new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            var genome = entities.GetComponent<GenomeComponent>(body);
            ProtoId<GeneticMutationPrototype> nativeId = nativeMutation;
            Assert.That(genome.InitialMutations, Does.Contain(nativeId));
            var round = genetics.GetRound();
            var nativeBlock = round.Mutations.IndexOf(nativeId);
            Assert.That(nativeBlock, Is.GreaterThanOrEqualTo(0));
            var nativeValue = round.Thresholds[nativeBlock];
            Assert.That(genetics.TrySetBlock((body, genome), nativeBlock, nativeValue, body), Is.True);
            var nativeStability = genome.Stability;
            var baseline = genome.Baseline.ToArray();

            Enable(entities, body, "GeneticNoBreathing");
            Enable(entities, body, "GeneticPhotophobia");
            Assert.That(genetics.HasGeneticModifications(body), Is.True);
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(genome.Active, Is.EquivalentTo(genome.InitialMutations));
            Assert.That(genome.Blocks[nativeBlock], Is.EqualTo(nativeValue), "Native blocks must be preserved exactly.");
            Assert.That(genome.Stability, Is.EqualTo(nativeStability));
            Assert.That(genome.Baseline, Is.EqualTo(baseline));
            Assert.That(genetics.HasGeneticModifications(body), Is.False);
            var revision = genome.Revision;
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(genome.Revision, Is.EqualTo(revision), "Further metabolism must not reconcile an unchanged genome.");

            Assert.That(genetics.TrySetBlock((body, genome), nativeBlock, 0, body), Is.True);
            Enable(entities, body, "GeneticNoBreathing");
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(genome.Blocks[nativeBlock], Is.Zero, "Genostabilin must not reactivate disabled native genes.");
            Assert.That(genome.Active.Contains(nativeId), Is.False);
            Assert.That(genome.Active.Contains("GeneticNoBreathing"), Is.False);
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
