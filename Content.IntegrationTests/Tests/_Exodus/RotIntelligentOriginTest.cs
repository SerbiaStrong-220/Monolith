using System;
using System.Linq;
using Content.Server._EinsteinEngines.Language;
using Content.Server._Exodus.Virology;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task RotCreaturesSpeakAndUnderstandOargoVoi()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var languages = em.System<LanguageSystem>();
            foreach (var prototype in new[]
                     {
                         "MobRotSpawn", "MobRotHungry", "MobRotSated", "MobRotLarva", "MobRotNester", "MobRotIntelligent",
                     })
            {
                var creature = em.SpawnEntity(prototype, map.GridCoords);
                Assert.That(languages.CanSpeak((creature, null), "OargoVoi"), Is.True, prototype);
                Assert.That(languages.CanUnderstand((creature, null), "OargoVoi"), Is.True, prototype);
                Assert.That(languages.GetLanguage((creature, null)).ID, Is.EqualTo("OargoVoi"), prototype);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FirstTwoRotVictimsSpawnCoresAndAllThreeKeepTheirBrood()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 3; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            var rule = em.SpawnEntity("ExodusRotRule", map.GridCoords);
            var virology = em.System<VirologySystem>();
            var mobState = em.System<MobStateSystem>();
            for (var i = 0; i < 3; i++)
            {
                var victim = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, i + .5f, .5f));
                Assert.That(virology.AddVirus(victim, "VirusRot"), Is.True);
                var strain = virology.GetStrains(victim).Single();
                strain.Comp.SymptomStates["RotSyndrome"].Stage = 3;
                virology.RefreshSymptoms(strain);
                var brood = em.GetComponent<VirusBroodComponent>(victim);
                Assert.That(brood.OffspringCount, Is.EqualTo(3));
                brood.MinDelay = brood.MaxDelay = TimeSpan.Zero;

                mobState.ChangeMobState(victim, MobState.Dead);
                Assert.That(em.EntityQuery<RotIntelligentComponent>().Count(), Is.EqualTo(Math.Min(i + 1, 2)));
                Assert.That(brood.Incubating, Is.True);
            }

            Assert.That(em.GetComponent<VirusEpidemicRuleComponent>(rule).IntelligentCoreVictims, Is.EqualTo(2));
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityQuery<VirusOffspringComponent>().Count(), Is.EqualTo(9),
                "Every victim should still release three randomly selected creatures after incubation.");
            Assert.That(em.EntityQuery<RotIntelligentComponent>().Count(), Is.EqualTo(2));
        });
        await pair.CleanReturnAsync();
    }
}
