using System.Collections.Generic;
using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Damage;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [Test]
    public async Task HulkFlatResistanceClampsDamagePreservesHealingAndStopsAfterReset()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            var genome = Enable(entities, body, "GeneticHulk");
            Enable(entities, body, "GeneticHulk");
            var hit = new DamageSpecifier
            {
                DamageDict = new()
                {
                    ["Blunt"] = 9,
                    ["Slash"] = 4,
                    ["Piercing"] = 5,
                    ["Heat"] = 12,
                    ["Shock"] = 6,
                    ["Cold"] = -3,
                    ["Caustic"] = 8,
                    ["Poison"] = 7,
                    ["Radiation"] = 2,
                },
            };
            var damage = new DamageModifyEvent(hit);
            entities.EventBus.RaiseLocalEvent(body, damage);
            Assert.That(damage.Damage.DamageDict.GetValueOrDefault("Blunt").Float(), Is.EqualTo(4));
            Assert.That(damage.Damage.DamageDict.GetValueOrDefault("Slash").Float(), Is.Zero);
            Assert.That(damage.Damage.DamageDict.GetValueOrDefault("Piercing").Float(), Is.Zero);
            Assert.That(damage.Damage.DamageDict.GetValueOrDefault("Heat").Float(), Is.EqualTo(7));
            Assert.That(damage.Damage.DamageDict.GetValueOrDefault("Shock").Float(), Is.EqualTo(1));
            Assert.That(damage.Damage.DamageDict.GetValueOrDefault("Cold").Float(), Is.EqualTo(-3));
            Assert.That(damage.Damage.DamageDict.GetValueOrDefault("Caustic").Float(), Is.EqualTo(3));
            Assert.That(damage.Damage.DamageDict.GetValueOrDefault("Poison").Float(), Is.EqualTo(7));
            Assert.That(damage.Damage.DamageDict.GetValueOrDefault("Radiation").Float(), Is.EqualTo(2));
            Assert.That(hit.DamageDict["Blunt"].Float(), Is.EqualTo(9), "The source damage must not be mutated.");

            Assert.That(genetics.TryStabilize(body), Is.True);
            damage = new DamageModifyEvent(hit);
            entities.EventBus.RaiseLocalEvent(body, damage);
            Assert.That(damage.Damage.DamageDict, Is.EquivalentTo(hit.DamageDict));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase("MobAsakim")]
    [TestCase("MobAsakimRandom")]
    public async Task AsakimStartWithGeneticDeflection(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity(prototype, new EntityCoordinates(map, Vector2.Zero));
            var genome = entities.GetComponent<GenomeComponent>(body);
            Assert.That(genome.Active.Contains("GeneticJump"), Is.True);
            Assert.That(genome.Active.Contains("GeneticGoJuiceDependency"), Is.True);
            Assert.That(genome.Stability, Is.EqualTo(genome.StabilityCapacity - 20 + 60 - 15));
            var state = entities.GetComponent<GeneticAbilityStateComponent>(body);
            Assert.That(state.Deflector, Is.Not.Null);
            Assert.That(entities.GetComponent<ReflectComponent>(state.Deflector!.Value).ReflectProb, Is.EqualTo(0.1f));
            var genetics = entities.System<GeneticsSystem>();
            var deflector = state.Deflector;
            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(genome.Active.Contains("GeneticJump"), Is.True);
            Assert.That(state.Deflector, Is.EqualTo(deflector));
            var block = genetics.GetRound().Mutations.IndexOf("GeneticJump");
            Assert.That(genetics.TrySetBlock((body, genome), block, 0, body), Is.True);
            Assert.That(genome.Active.Contains("GeneticJump"), Is.False);
            Assert.That(state.Deflector, Is.Null);
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
