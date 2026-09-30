using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [Test]
    public async Task BloodBathRemovesFullCapacityFractionOnScheduleAndStopsAfterReset()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genome = Enable(entities, body, "GeneticBloodBath");
            var state = entities.GetComponent<GeneticAbilityStateComponent>(body);
            var blood = entities.GetComponent<BloodstreamComponent>(body);
            var bloodstream = entities.System<BloodstreamSystem>();
            var abilities = entities.System<GeneticAbilitiesSystem>();
            var genetics = entities.System<GeneticsSystem>();
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(state.NextBloodExpulsion, Is.EqualTo(now + TimeSpan.FromSeconds(30)));

            abilities.Update(0);
            Assert.That(bloodstream.GetBloodLevelPercentage(body, blood), Is.EqualTo(1f));
            Assert.That(blood.BleedAmount, Is.Zero);
            state.NextBloodExpulsion = now;
            abilities.Update(0);
            Assert.That(bloodstream.GetBloodLevelPercentage(body, blood), Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(blood.BleedAmount, Is.EqualTo(1f));
            abilities.Update(0);
            Assert.That(bloodstream.GetBloodLevelPercentage(body, blood), Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(blood.BleedAmount, Is.EqualTo(1f));

            // Re-evaluating unrelated genes must not postpone an already scheduled burst.
            state.NextBloodExpulsion = now + TimeSpan.FromSeconds(5);
            genetics.Reconcile((body, genome));
            Assert.That(state.NextBloodExpulsion, Is.EqualTo(now + TimeSpan.FromSeconds(5)));
            state.NextBloodExpulsion = now;
            abilities.Update(0);
            Assert.That(bloodstream.GetBloodLevelPercentage(body, blood), Is.EqualTo(0.8f).Within(0.001f),
                "Each burst costs 10% of capacity, not 10% of the remaining blood.");
            Assert.That(blood.BleedAmount, Is.EqualTo(2f));

            Assert.That(genetics.TryStabilize(body), Is.True);
            state.NextBloodExpulsion = now;
            abilities.Update(0);
            Assert.That(bloodstream.GetBloodLevelPercentage(body, blood), Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(blood.BleedAmount, Is.EqualTo(2f), "Reset stops new bleeding without healing existing bleeding.");
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
