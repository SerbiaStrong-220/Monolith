using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server._Exodus.Virology;
using Content.Server._Exodus.Virology.Behaviors;
using Content.Server.Radiation.Systems;
using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Behaviors;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(VirusRadiophasiaSystem))]
public sealed class VirusRadiophasiaTest
{
    [TestCase(0f, 0f)]
    [TestCase(2f, 2f)]
    public async Task StageOneAmbientRadiationOnlyHealsForExternalExposure(float externalRads, float expectedHealing)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            try
            {
                var host = SpawnInfectedHost(entities, new EntityCoordinates(map, Vector2.Zero));
                var radiophasia = entities.GetComponent<VirusRadiophasiaComponent>(host);
                var damage = entities.System<DamageableSystem>();
                var damageable = entities.GetComponent<DamageableComponent>(host);
                var before = damageable.Damage.DamageDict["Blunt"];

                // Gridcast includes the host's own source in its total. Call the real event chain
                // synchronously so no unrelated radiation, metabolism or symptom progression runs.
                entities.System<RadiationSystem>().IrradiateEntity(host,
                    radiophasia.RadiationIntensity + externalRads, 1f);

                var expected = before - FixedPoint2.New(expectedHealing) * damage.UniversalAllHealModifier;
                Assert.That(damageable.Damage.DamageDict["Blunt"], Is.EqualTo(expected),
                    "Own radiation must not heal, and external irradiation must not also trigger direct-damage healing.");
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task StageOneDirectRadiationDamageStillHealsRegardlessOfOrigin(bool selfOrigin)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            try
            {
                var host = SpawnInfectedHost(entities, new EntityCoordinates(map, Vector2.Zero));
                var damage = entities.System<DamageableSystem>();
                var damageable = entities.GetComponent<DamageableComponent>(host);
                var before = damageable.Damage.DamageDict["Blunt"];

                // Chemistry and other direct damage sources do not raise OnIrradiatedEvent.
                // A self origin alone must not classify their Radiation damage as ambient exposure.
                damage.TryChangeDamage(host, new DamageSpecifier
                {
                    DamageDict = new() { ["Radiation"] = 2 },
                }, origin: selfOrigin ? host : null);

                var expected = before - FixedPoint2.New(0.6f) * damage.UniversalAllHealModifier;
                Assert.That(damageable.Damage.DamageDict["Blunt"], Is.EqualTo(expected),
                    "First-stage radiophasia must retain its 30% direct-radiation healing.");
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnInfectedHost(IEntityManager entities, EntityCoordinates coordinates)
    {
        var host = entities.SpawnEntity("MobHuman", coordinates);
        // Keep random radiation mutations from changing the host during this isolated symptom test.
        entities.AddComponent<GeneticIncompatibleComponent>(host);
        entities.System<DamageableSystem>().TryChangeDamage(host, new DamageSpecifier
        {
            DamageDict = new() { ["Blunt"] = 20 },
        }, ignoreResistances: true, ignoreGlobalModifiers: true);

        Assert.That(entities.System<VirologySystem>().AddVirus(host, new VirusDescriptor
        {
            Name = "Radiophasia radiation regression strain",
            Genome = VirusGenome.Dna,
            Symptoms = [new() { Symptom = "Radiophasia" }],
        }), Is.True);
        Assert.That(entities.HasComponent<VirusRadiophasiaComponent>(host), Is.True);
        return host;
    }
}
