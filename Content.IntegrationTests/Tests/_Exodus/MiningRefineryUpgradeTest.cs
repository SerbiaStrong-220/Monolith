using System.Collections.Generic;
using System.IO;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Construction.Components;
using Content.Shared.Materials;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class MiningRefineryUpgradeTest
{
    [TestCase("MatterBinStockPart", 1f, false)]
    [TestCase("AdvancedMatterBinStockPart", 1.5f, false)]
    [TestCase("SuperMatterBinStockPart", 2f, false)]
    [TestCase("BluespaceMatterBinStockPart", 2.5f, false)]
    [TestCase("SuperMatterBinStockPart", 1.5f, true)]
    public async Task PartsResizeBothBuffersWithoutCompoundingOrChangingContents(string prototype, float multiplier, bool alternate)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var uid = em.SpawnEntity("BulkMiningRefinery", map.GridCoords);
            var refinery = em.GetComponent<MiningRefineryComponent>(uid);
            var storage = em.GetComponent<MaterialStorageComponent>(uid);
            var capacity = storage.StorageLimit!.Value;
            var volume = refinery.Exhaust.Volume;
            var corrosion = refinery.CorrosionThreshold;
            var explosion = refinery.ExplosionThreshold;
            refinery.Exhaust.AdjustMoles(refinery.ExhaustGas, 50);
            var pressure = refinery.Exhaust.Pressure;
            Assert.That(em.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(uid, refinery.SlurryMaterial, 500), Is.True);

            SetMatterBins(em, uid, prototype, alternate);
            var machine = em.GetComponent<MachineComponent>(uid);
            for (var i = 0; i < 3; i++)
                em.System<ConstructionSystem>().RefreshParts(uid, machine);

            Assert.Multiple(() =>
            {
                Assert.That(storage.StorageLimit, Is.EqualTo((int)(capacity * multiplier)));
                Assert.That(refinery.Exhaust.Volume, Is.EqualTo(volume * multiplier));
                Assert.That(refinery.CorrosionThreshold, Is.EqualTo(corrosion * multiplier));
                Assert.That(refinery.ExplosionThreshold, Is.EqualTo(explosion * multiplier));
                Assert.That(refinery.Exhaust.TotalMoles, Is.EqualTo(50));
                Assert.That(refinery.Exhaust.Pressure, Is.EqualTo(pressure / multiplier).Within(0.001f));
                Assert.That(storage.Storage[refinery.SlurryMaterial], Is.EqualTo(500));
                Assert.That(refinery.StorageState.SlurryStored, Is.EqualTo(500));
                Assert.That(refinery.StorageState.SlurryCapacity, Is.EqualTo(storage.StorageLimit));
                Assert.That(refinery.StorageState.GasMoles, Is.EqualTo(50));
            });
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DowngradePreservesExcessMetalAndAllowsConsumptionWithoutAcceptingMore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var uid = em.SpawnEntity("BulkMiningRefinery", map.GridCoords);
            var refinery = em.GetComponent<MiningRefineryComponent>(uid);
            var storage = em.GetComponent<MaterialStorageComponent>(uid);
            var materials = em.System<SharedMaterialStorageSystem>();
            var capacity = storage.StorageLimit!.Value;
            SetMatterBins(em, uid, "SuperMatterBinStockPart");
            Assert.That(materials.TryChangeMaterialAmount(uid, refinery.SlurryMaterial, capacity * 2), Is.True);

            SetMatterBins(em, uid, "MatterBinStockPart");
            Assert.That(storage.StorageLimit, Is.EqualTo(capacity));
            Assert.That(storage.Storage[refinery.SlurryMaterial], Is.EqualTo(capacity * 2));
            Assert.That(materials.TryChangeMaterialAmount(uid, refinery.SlurryMaterial, 1), Is.False);
            Assert.That(materials.TryChangeMaterialAmount(uid, refinery.SlurryMaterial, -100), Is.True);
            Assert.That(storage.Storage[refinery.SlurryMaterial], Is.EqualTo(capacity * 2 - 100));

            var withdrawal = new Dictionary<ProtoId<MaterialPrototype>, int> { [refinery.SlurryMaterial] = -100 };
            Assert.That(materials.TryChangeMaterialAmount((uid, storage), withdrawal), Is.True);
            Assert.That(storage.Storage[refinery.SlurryMaterial], Is.EqualTo(capacity * 2 - 200));
            Assert.That(materials.TryChangeMaterialAmount(uid, refinery.SlurryMaterial, -capacity), Is.True);
            Assert.That(storage.Storage[refinery.SlurryMaterial], Is.EqualTo(capacity - 200));
            Assert.That(materials.TryChangeMaterialAmount(uid, refinery.SlurryMaterial, 200), Is.True);
            Assert.That(materials.TryChangeMaterialAmount(uid, refinery.SlurryMaterial, 1), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SavingUpgradedMachinePreservesBaselinesAndContents()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var uid = em.SpawnEntity("BulkMiningRefinery", map.GridCoords);
            var refinery = em.GetComponent<MiningRefineryComponent>(uid);
            refinery.Exhaust.AdjustMoles(refinery.ExhaustGas, 50);
            Assert.That(em.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(uid, refinery.SlurryMaterial, 500), Is.True);
            SetMatterBins(em, uid, "AdvancedMatterBinStockPart");
            var capacity = em.GetComponent<MaterialStorageComponent>(uid).StorageLimit;
            var volume = refinery.Exhaust.Volume;
            var corrosion = refinery.CorrosionThreshold;
            var explosion = refinery.ExplosionThreshold;

            var loader = em.System<MapLoaderSystem>();
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid.Owner, writer), Is.True);
            em.DeleteEntity(map.Grid.Owner);
            using var reader = new StringReader(writer.ToString());
            Assert.That(loader.TryLoadGrid(reader, "mining-refinery-upgrade-test", out _, out var grid,
                DeserializationOptions.Default with { InitializeMaps = true }), Is.True);

            var found = 0;
            var query = em.AllEntityQueryEnumerator<MiningRefineryComponent, TransformComponent>();
            while (query.MoveNext(out var loaded, out var comp, out var xform))
            {
                if (xform.GridUid != grid!.Value.Owner)
                    continue;

                found++;
                Assert.That(comp.CapacityMultiplier, Is.EqualTo(1.5f), "Loading must preserve the upgrade examine multiplier.");
                em.System<ConstructionSystem>().RefreshParts(loaded, em.GetComponent<MachineComponent>(loaded));
                Assert.Multiple(() =>
                {
                    Assert.That(em.GetComponent<MaterialStorageComponent>(loaded).StorageLimit, Is.EqualTo(capacity));
                    Assert.That(comp.Exhaust.Volume, Is.EqualTo(volume));
                    Assert.That(comp.CorrosionThreshold, Is.EqualTo(corrosion));
                    Assert.That(comp.ExplosionThreshold, Is.EqualTo(explosion));
                    Assert.That(comp.Exhaust.TotalMoles, Is.EqualTo(50));
                    Assert.That(comp.StorageState.SlurryStored, Is.EqualTo(500));
                    Assert.That(comp.CapacityMultiplier, Is.EqualTo(1.5f));
                });
            }
            Assert.That(found, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    private static void SetMatterBins(IEntityManager em, EntityUid uid, string prototype, bool alternate = false)
    {
        var machine = em.GetComponent<MachineComponent>(uid);
        var containers = em.System<SharedContainerSystem>();
        var index = 0;
        foreach (var part in new List<EntityUid>(machine.PartContainer.ContainedEntities))
        {
            if (!em.TryGetComponent<MachinePartComponent>(part, out var comp) || comp.PartType != "MatterBin")
                continue;

            if (alternate && index++ % 2 != 0)
                continue;

            em.DeleteEntity(part);
            var replacement = em.SpawnEntity(prototype, em.GetComponent<TransformComponent>(uid).Coordinates);
            Assert.That(containers.Insert(replacement, machine.PartContainer), Is.True);
        }
        em.System<ConstructionSystem>().RefreshParts(uid, machine);
    }
}
