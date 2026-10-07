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
    [TestCase(1, 0f, 0f)]
    [TestCase(2, 0f, 0f)]
    [TestCase(3, 0f, 0f)]
    [TestCase(1, 2f, 0.667f)]
    [TestCase(2, 2f, 0.667f)]
    [TestCase(3, 2f, 0.667f)]
    [TestCase(1, 100f, 0.99f)]
    [TestCase(2, 100f, 0.99f)]
    [TestCase(3, 100f, 0.99f)]
    public async Task AmbientRadiationExcludesSelfAndCapsHealing(int stage, float externalRads, float healing)
    {
        await WithHost(stage, (entities, host) =>
        {
            var symptom = entities.GetComponent<VirusRadiophasiaComponent>(host);
            var totalRads = symptom.RadiationIntensity + externalRads;
            // Exercise the real event chain without unrelated metabolism or symptom progression.
            entities.System<RadiationSystem>().IrradiateEntity(host, totalRads, 1f);
            AssertDamage(entities, host, healing, totalRads * 0.05f);
        });
    }

    [TestCase(1, 2, 0.375f, false)]
    [TestCase(1, 2, 0.375f, true)]
    [TestCase(2, 2, 0.5f, false)]
    [TestCase(3, 2, 0.583f, false)]
    [TestCase(1, 100, 0.968f, false)]
    [TestCase(2, 100, 0.98f, false)]
    [TestCase(3, 100, 0.986f, false)]
    public async Task DirectRadiationHealsOnceAndRetainsFivePercentDamage(int stage, int rads, float healing, bool selfOrigin)
    {
        await WithHost(stage, (entities, host) =>
        {
            IrradiateDirectly(entities, host, rads, selfOrigin);
            AssertDamage(entities, host, healing, rads * 0.05f);
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public async Task AmbientAndDirectRadiationShareOneHealingBudget(int stage)
    {
        await WithHost(stage, (entities, host) =>
        {
            IrradiateDirectly(entities, host, 2);
            var symptom = entities.GetComponent<VirusRadiophasiaComponent>(host);
            var totalRads = symptom.RadiationIntensity + 2f;
            entities.System<RadiationSystem>().IrradiateEntity(host, totalRads, 1f);
            IrradiateDirectly(entities, host, 100);
            AssertDamage(entities, host, 0.97f, (102f + totalRads) * 0.05f);
        });
    }

    [Test]
    public async Task RepeatedRadiationHasDecreasingMarginalHealing()
    {
        await WithHost(1, (entities, host) =>
        {
            var damage = entities.GetComponent<DamageableComponent>(host).Damage.DamageDict;
            var before = damage["Blunt"];
            IrradiateDirectly(entities, host, 1);
            var afterFirst = damage["Blunt"];
            AssertDamage(entities, host, 0.231f, 0.05f);
            IrradiateDirectly(entities, host, 1);
            var secondHealing = afterFirst - damage["Blunt"];

            Assert.That(secondHealing, Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(secondHealing, Is.LessThan(before - afterFirst));
            AssertDamage(entities, host, 0.375f, 0.1f);
        });
    }

    [Test]
    public async Task MultipleStrainsDoNotStackHealingOrProtectionAndCureRestoresDamage()
    {
        await WithHost(1, (entities, host) =>
        {
            var virology = entities.System<VirologySystem>();
            Assert.That(virology.AddVirus(host, new VirusDescriptor
            {
                Name = "Distinct radiophasia regression strain",
                Genome = VirusGenome.Dna,
                // Ordinary strains with the same genome merge instead of coexisting.
                IsSupervirus = true,
                Symptoms = [new() { Symptom = "Radiophasia" }, new() { Symptom = "BreathInversion" }],
            }), Is.True);
            Assert.That(virology.GetStrains(host), Has.Count.EqualTo(2));
            IrradiateDirectly(entities, host, 2);
            AssertDamage(entities, host, 0.375f, 0.1f);

            virology.RemoveVirus(virology.GetStrains(host)[0]);
            Assert.That(entities.HasComponent<VirusRadiophasiaComponent>(host), Is.True);
            IrradiateDirectly(entities, host, 100);
            AssertDamage(entities, host, 0.968f, 5.1f);

            virology.RemoveVirus(virology.GetStrains(host)[0]);
            Assert.That(entities.HasComponent<VirusRadiophasiaComponent>(host), Is.False);
            IrradiateDirectly(entities, host, 2);
            AssertDamage(entities, host, 0.968f, 7.1f);
        });
    }

    private static void IrradiateDirectly(IEntityManager entities, EntityUid host, int rads, bool selfOrigin = false)
    {
        entities.System<DamageableSystem>().TryChangeDamage(host, new DamageSpecifier
        {
            DamageDict = new() { ["Radiation"] = rads },
        }, origin: selfOrigin ? host : null);
    }

    private static void AssertDamage(IEntityManager entities, EntityUid host, float healing, float radiationDamage)
    {
        var system = entities.System<DamageableSystem>();
        var damage = entities.GetComponent<DamageableComponent>(host).Damage.DamageDict;
        // Group distribution and global modifiers round each individual fixed-point value.
        var tolerance = 0.04f * Math.Max(1f, system.UniversalAllHealModifier);
        var brute = damage["Blunt"] + damage["Slash"] + damage["Piercing"];
        var burn = damage["Heat"] + damage["Shock"] + damage["Cold"] + damage["Caustic"];
        Assert.Multiple(() =>
        {
            Assert.That(brute.Float(), Is.EqualTo(15f - healing * system.UniversalAllHealModifier).Within(tolerance));
            Assert.That(burn.Float(), Is.EqualTo(20f - healing * system.UniversalAllHealModifier).Within(tolerance));
            Assert.That(brute.Float(), Is.GreaterThanOrEqualTo(15f - system.UniversalAllHealModifier - 0.001f));
            Assert.That(burn.Float(), Is.GreaterThanOrEqualTo(20f - system.UniversalAllHealModifier - 0.001f));
            if (healing == 0f)
            {
                Assert.That(brute, Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(burn, Is.EqualTo(FixedPoint2.New(20)));
            }

            Assert.That(damage["Radiation"].Float(),
                Is.EqualTo(5f + radiationDamage * system.UniversalAllDamageModifier).Within(0.04f));
            Assert.That(damage["Poison"], Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(damage["Cellular"], Is.EqualTo(FixedPoint2.New(5)));
        });
    }

    private static async Task WithHost(int stage, Action<IEntityManager, EntityUid> assertion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            try
            {
                var host = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
                entities.AddComponent<GeneticIncompatibleComponent>(host);
                entities.System<DamageableSystem>().TryChangeDamage(host, new DamageSpecifier
                {
                    DamageDict = new()
                    {
                        ["Blunt"] = 5, ["Slash"] = 5, ["Piercing"] = 5,
                        ["Heat"] = 5, ["Shock"] = 5, ["Cold"] = 5, ["Caustic"] = 5,
                        ["Radiation"] = 5, ["Poison"] = 5, ["Cellular"] = 5,
                    },
                }, ignoreResistances: true, ignoreGlobalModifiers: true);

                var virology = entities.System<VirologySystem>();
                Assert.That(virology.AddVirus(host, new VirusDescriptor
                {
                    Name = "Radiophasia radiation regression strain",
                    Genome = VirusGenome.Dna,
                    Symptoms = [new() { Symptom = "Radiophasia" }],
                }), Is.True);
                for (var current = 1; current < stage; current++)
                    Assert.That(virology.ForceAdvanceAllSymptoms(host), Is.EqualTo(1));

                Assert.That(entities.HasComponent<VirusRadiophasiaComponent>(host), Is.True);
                assertion(entities, host);
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
