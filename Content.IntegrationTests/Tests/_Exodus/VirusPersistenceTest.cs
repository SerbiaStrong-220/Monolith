using System;
using System.IO;
using System.Linq;
using Content.Server._Exodus.Virology;
using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Behaviors;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Radiation.Components;
using Content.Server.Radiation.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class VirusPersistenceTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: VirusPersistenceHost
          parent: MobHuman
          save: true
        """;

    private sealed class LiveContext : ITestContextLike
    {
        public string FullName { get; } = TestContext.CurrentContext.Test.FullName;
        public TextWriter Out { get; } = TextWriter.Synchronized(TestContext.Progress);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LoadingDoesNotApplyBonusesTwiceAndCureRestoresOriginalEffects(bool innate)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var host = em.SpawnEntity("VirusPersistenceHost", map.GridCoords);
            var grid = map.Grid.Owner;
            var viruses = em.System<VirologySystem>();
            var thresholds = em.System<MobThresholdSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            Assert.That(thresholds.TryGetThresholdForState(host, MobState.Critical, out var originalThreshold), Is.True);
            if (innate)
            {
                em.EnsureComponent<PointLightComponent>(host);
                em.System<SharedPointLightSystem>().SetRadius(host, 4);
                em.EnsureComponent<RadiationSourceComponent>(host);
                em.System<RadiationSystem>().SetIntensity(host, 2);
            }
            Assert.That(viruses.AddVirus(host, new VirusDescriptor
            {
                Name = "Persistence regression strain",
                Genome = VirusGenome.Dna,
                Symptoms =
                [
                    new() { Symptom = "Radiophasia" },
                    new() { Symptom = "DogVitality" },
                    new() { Symptom = "BloodVomiting" },
                ],
            }), Is.True);
            Assert.That(thresholds.TryGetThresholdForState(host, MobState.Critical, out var infectedThreshold), Is.True);
            Assert.That(infectedThreshold, Is.GreaterThan(originalThreshold));
            em.GetComponent<BloodVomitComponent>(host).NextVomit = timing.CurTime + TimeSpan.FromSeconds(7);
            for (var i = 0; i < 2; i++)
            {
                var loader = em.System<MapLoaderSystem>();
                using var text = new StringWriter();
                Assert.That(loader.TrySaveGrid(grid, text), Is.True);
                em.DeleteEntity(grid);
                using var reader = new StringReader(text.ToString());
                Assert.That(loader.TryLoadGrid(reader, "virus-effects-save-test", out _, out var loadedGrid,
                    DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
                grid = loadedGrid!.Value.Owner;
                var holders = em.AllEntityQueryEnumerator<VirusHolderComponent, TransformComponent>();
                var loadedHosts = 0;
                while (holders.MoveNext(out var loadedHost, out _, out var transform))
                {
                    if (transform.GridUid != grid)
                        continue;
                    host = loadedHost;
                    loadedHosts++;
                }
                Assert.That(loadedHosts, Is.EqualTo(1));
                Assert.That(thresholds.TryGetThresholdForState(host, MobState.Critical, out var loadedThreshold), Is.True);
                Assert.That(loadedThreshold, Is.EqualTo(infectedThreshold));
                Assert.That((em.GetComponent<BloodVomitComponent>(host).NextVomit - timing.CurTime).TotalSeconds,
                    Is.EqualTo(7).Within(.1));
            }
            viruses.RemoveVirus(viruses.GetStrains(host).Single());
            Assert.That(thresholds.TryGetThresholdForState(host, MobState.Critical, out var curedThreshold), Is.True);
            Assert.That(curedThreshold, Is.EqualTo(originalThreshold));
            Assert.That(em.HasComponent<PointLightComponent>(host), Is.EqualTo(innate));
            Assert.That(em.HasComponent<RadiationSourceComponent>(host), Is.EqualTo(innate));
            if (innate)
            {
                Assert.That(em.GetComponent<PointLightComponent>(host).Radius, Is.EqualTo(4));
                Assert.That(em.GetComponent<RadiationSourceComponent>(host).Intensity, Is.EqualTo(2));
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SavedInfectionsKeepProgressSuppressionAndSymptomOwnership(bool paused)
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var hosts = new EntityUid[4];
        var strainIds = new EntityUid[4];
        var timing = server.ResolveDependency<IGameTiming>();
        TimeSpan savedAt = default;
        await server.WaitAssertion(() =>
        {
            var viruses = em.System<VirologySystem>();
            savedAt = timing.CurTime;
            for (var i = 0; i < hosts.Length; i++)
            {
                var host = hosts[i] = em.SpawnEntity(null, map.GridCoords);
                em.EnsureComponent<VirusSusceptibleComponent>(host);
                if (i == 3)
                    em.EnsureComponent<BloodVomitComponent>(host);
                Assert.That(viruses.AddVirus(host, "VirusCrimsonFever"), Is.True);
                var strain = viruses.GetStrains(host).Single();
                strainIds[i] = strain;
                if (i != 0)
                    viruses.ForceAdvanceAllSymptoms(host);
                strain.Comp.SymptomStates["BloodVomiting"].StageStartTime = savedAt - TimeSpan.FromSeconds(37);
                strain.Comp.SymptomStates["BloodVomiting"].LastEmote = savedAt - TimeSpan.FromSeconds(9);
                strain.Comp.SymptomStates["BloodVomiting"].EmoteDelay = TimeSpan.FromSeconds(60);
                strain.Comp.SymptomStates["BloodVomiting"].Revealed = true;
                strain.Comp.SymptomTimeMultiplier = 2;
                strain.Comp.Cure = new VirusCure { Reagents = ["Water"] };
                strain.Comp.NextEffect = savedAt + TimeSpan.FromSeconds(23);
                if (i == 0)
                {
                    strain.Comp.IncubationEndsAt = savedAt + TimeSpan.FromSeconds(91);
                    strain.Comp.HiddenUntil = savedAt + TimeSpan.FromSeconds(41);
                }
                if (i == 2)
                    viruses.SuppressVirus(strain, TimeSpan.FromSeconds(83));
                if (paused)
                    em.System<MetaDataSystem>().SetEntityPaused(host, true);
            }
        });
        if (paused)
            await pair.RunSeconds(10);
        await server.WaitAssertion(() =>
        {
            var viruses = em.System<VirologySystem>();
            var cure = em.GetComponent<VirusComponent>(strainIds[1]).Cure!.Reagents.ToArray();
            var accelerant = em.GetComponent<VirusComponent>(strainIds[1]).SymptomStates["BloodVomiting"].Accelerant;
            var loader = em.System<MapLoaderSystem>();
            using var text = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid.Owner, text), Is.True);
            Assert.That(em.GetComponent<VirusHolderComponent>(hosts[1]).SavedViruses, Is.Empty);
            Assert.That(em.GetComponent<VirusComponent>(strainIds[1]).SymptomStates["BloodVomiting"].StageStartTime,
                Is.EqualTo(savedAt - TimeSpan.FromSeconds(37)), "Saving must not alter the running infection.");
            em.DeleteEntity(map.Grid);
            using var reader = new StringReader(text.ToString());
            Assert.That(loader.TryLoadGrid(reader, "virus-persistence-test", out _, out var loadedGrid,
                DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
            var innateCount = 0;
            var activeCount = 0;
            var suppressedCount = 0;
            var incubatingCount = 0;
            var query = em.AllEntityQueryEnumerator<VirusHolderComponent, TransformComponent>();
            while (query.MoveNext(out var host, out var holder, out var transform))
            {
                if (transform.GridUid != loadedGrid!.Value.Owner)
                    continue;
                var strain = viruses.GetStrains(host).Single();
                var symptom = strain.Comp.SymptomStates["BloodVomiting"];
                Assert.That(strain.Comp.Carrier, Is.EqualTo(host));
                Assert.That(strain.Comp.SymptomTimeMultiplier, Is.EqualTo(2));
                Assert.That(strain.Comp.Cure!.Reagents, Is.EqualTo(cure));
                Assert.That(viruses.ResolveCure(viruses.ToDescriptor(strain))!.Reagents, Is.EqualTo(cure));
                Assert.That(symptom.Revealed, Is.True);
                Assert.That((timing.CurTime - symptom.StageStartTime).TotalSeconds, Is.EqualTo(37).Within(.1));
                Assert.That((timing.CurTime - symptom.LastEmote).TotalSeconds, Is.EqualTo(9).Within(.1));
                Assert.That(symptom.EmoteDelay.TotalSeconds, Is.EqualTo(60));
                Assert.That((strain.Comp.NextEffect - timing.CurTime).TotalSeconds, Is.EqualTo(23).Within(.1));
                if (strain.Comp.IncubationEndsAt is { } incubation)
                {
                    incubatingCount++;
                    Assert.That((incubation - timing.CurTime).TotalSeconds, Is.EqualTo(91).Within(.1));
                    Assert.That((strain.Comp.HiddenUntil!.Value - timing.CurTime).TotalSeconds, Is.EqualTo(41).Within(.1));
                    Assert.That(symptom.Stage, Is.Zero);
                    Assert.That(em.HasComponent<BloodVomitComponent>(host), Is.False);
                }
                else if (strain.Comp.SuppressedUntil is { } suppression)
                {
                    suppressedCount++;
                    Assert.That((suppression - timing.CurTime).TotalSeconds, Is.EqualTo(83).Within(.1));
                    Assert.That(em.HasComponent<BloodVomitComponent>(host), Is.False);
                }
                else
                {
                    activeCount++;
                    Assert.That(symptom.Stage, Is.EqualTo(1));
                    var owned = holder.GrantedComponents.ContainsKey("BloodVomit");
                    if (owned)
                        Assert.That(symptom.Accelerant, Is.EqualTo(accelerant));
                    else
                        innateCount++;
                    Assert.That(em.HasComponent<BloodVomitComponent>(host), Is.True);
                    viruses.RemoveVirus(strain);
                    Assert.That(em.HasComponent<BloodVomitComponent>(host), Is.EqualTo(!owned));
                }
            }
            Assert.That((incubatingCount, suppressedCount, activeCount, innateCount), Is.EqualTo((1, 1, 2, 1)));
        });
        await pair.CleanReturnAsync();
    }
}
