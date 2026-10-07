using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.RegularExpressions;
using Content.Server._Exodus.Mining.Pipes;
using Content.Server._Exodus.Nebula;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Lathe;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Shared._DV.Construction;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared._Exodus.Nebula;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class MiningRefineryLargeTest
{
    private static readonly Regex _exhaustMultiplierField = new(@"(?m)^[ \t]*exhaustMultiplier:.*\r?\n");

    [TestCase("MatterBinStockPart", "MicroManipulatorStockPart", 1f, 1f)]
    [TestCase("AdvancedMatterBinStockPart", "NanoManipulatorStockPart", 1.06f, 0.94f)]
    [TestCase("SuperMatterBinStockPart", "PicoManipulatorStockPart", 1.12f, 0.88f)]
    [TestCase("BluespaceMatterBinStockPart", "FemtoManipulatorStockPart", 1.18f, 0.82f)]
    public async Task LargeRefineryHasGentlePartUpgradesAndBothSizesIncreaseExhaust(
        string bin, string manipulator, float capacityMultiplier, float costMultiplier)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var small = em.SpawnEntity("BulkMiningRefinery", map.GridCoords);
            var large = em.SpawnEntity("BulkMiningRefineryLarge", map.GridCoords);
            var smallRefinery = em.GetComponent<MiningRefineryComponent>(small);
            var refinery = em.GetComponent<MiningRefineryComponent>(large);
            var storage = em.GetComponent<MaterialStorageComponent>(large);
            Assert.That(refinery.Exhaust.Volume, Is.EqualTo(smallRefinery.Exhaust.Volume * 5));
            Assert.That(storage.StorageLimit, Is.EqualTo(em.GetComponent<MaterialStorageComponent>(small).StorageLimit * 5));

            ReplaceParts(em, small, bin, manipulator);
            ReplaceParts(em, large, bin, manipulator);
            for (var i = 0; i < 3; i++)
                em.System<ConstructionSystem>().RefreshParts(large, em.GetComponent<MachineComponent>(large));

            var lathe = em.GetComponent<LatheComponent>(large);
            Assert.Multiple(() =>
            {
                Assert.That(storage.StorageLimit, Is.EqualTo(3000000 * capacityMultiplier).Within(1));
                Assert.That(refinery.Exhaust.Volume, Is.EqualTo(5000 * capacityMultiplier).Within(0.001));
                Assert.That(refinery.CorrosionThreshold, Is.EqualTo(5000 * capacityMultiplier).Within(0.001));
                Assert.That(refinery.ExplosionThreshold, Is.EqualTo(10000 * capacityMultiplier).Within(0.001));
                Assert.That(lathe.FinalMaterialUseMultiplier, Is.EqualTo(costMultiplier).Within(0.00001));
                Assert.That(lathe.FinalTimeMultiplier, Is.EqualTo(costMultiplier).Within(0.00001));
            });

            ProtoId<LatheRecipePrototype> recipeId = "BulkMiningSteelOre";
            var printing = new LatheStartPrintingEvent(pair.Server.ProtoMan.Index(recipeId));
            em.EventBus.RaiseLocalEvent(small, ref printing);
            em.EventBus.RaiseLocalEvent(large, ref printing);
            Assert.That(smallRefinery.Exhaust.TotalMoles, Is.EqualTo(10 * capacityMultiplier).Within(0.001));
            Assert.That(refinery.Exhaust.TotalMoles, Is.EqualTo(20 * capacityMultiplier).Within(0.001));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ProductionChargesDiscountedPriceWearsFiltersAndKeepsDepletedCartridgesVisible()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid uid = default;
        var filters = new List<EntityUid>();
        await pair.Server.WaitAssertion(() =>
        {
            uid = em.SpawnEntity("BulkMiningRefineryLarge", map.GridCoords);
            var refinery = em.GetComponent<MiningRefineryComponent>(uid);
            var slots = em.System<ItemSlotsSystem>();
            var wear = em.System<NebulaGasSiphonSystem>();
            for (var i = 0; i < 4; i++)
            {
                var filter = em.SpawnEntity(i % 2 == 0 ? "DrakeRefineryFilter" : "NebulaGasSiphonFilter", map.GridCoords);
                filters.Add(filter);
                var comp = em.GetComponent<NebulaGasSiphonFilterComponent>(filter);
                if (i == 0)
                    wear.ConsumeFilter((filter, comp), comp.Remaining - 5);
                Assert.That(slots.TryInsert(uid, refinery.FilterSlots[i], filter, null), Is.True);
            }

            var extra = em.SpawnEntity("DrakeRefineryFilter", map.GridCoords);
            Assert.That(slots.TryInsert(uid, "filter5", extra, null), Is.False);
            Assert.That(refinery.ActiveFilters, Is.EqualTo(4));
            Assert.That(refinery.EfficiencyMultiplier, Is.EqualTo(0.8f).Within(0.00001));
            var storage = em.GetComponent<MaterialStorageComponent>(uid);
            var capacity = storage.StorageLimit!.Value;
            Assert.That(em.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(uid, refinery.SlurryMaterial, capacity), Is.True);
            Assert.That(refinery.FullnessDiscount, Is.EqualTo(0.2f).Within(0.00001));
            Assert.That(em.GetComponent<LatheComponent>(uid).FinalMaterialUseMultiplier, Is.EqualTo(0.64f).Within(0.00001));

            em.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
            var lathes = em.System<LatheSystem>();
            ProtoId<LatheRecipePrototype> recipeId = "BulkMiningSteelOre";
            Assert.That(lathes.TryAddToQueue(uid, pair.Server.ProtoMan.Index(recipeId), 1), Is.True);
            Assert.That(lathes.TryStartProducing(uid), Is.True);
            Assert.Multiple(() =>
            {
                // Float precision puts 100 * (0.8f * 0.8f) just above 64, so the lathe rounds up to 65.
                Assert.That(storage.Storage[refinery.SlurryMaterial], Is.EqualTo(capacity - 65));
                Assert.That(refinery.Exhaust.TotalMoles, Is.EqualTo(20));
                Assert.That(em.GetComponent<NebulaGasSiphonFilterComponent>(filters[0]).Remaining, Is.Zero);
                Assert.That(em.GetComponent<NebulaGasSiphonFilterComponent>(filters[1]).Remaining, Is.EqualTo(2495));
                Assert.That(refinery.ActiveFilters, Is.EqualTo(3));
                Assert.That(refinery.InstalledFilters, Is.EqualTo(4));
                Assert.That(refinery.StorageState.FilterDiscount, Is.EqualTo(0.15f).Within(0.00001));
            });
            Assert.That(em.System<SharedAppearanceSystem>().TryGetData<MiningRefineryFilterAppearance>(
                uid, MiningRefineryVisuals.Filters, out var appearance), Is.True);
            Assert.That(appearance.States["filter1"], Is.EqualTo(MiningRefineryFilterState.Depleted));
            Assert.That(appearance.States["filter2"], Is.EqualTo(MiningRefineryFilterState.Intact));
            Assert.That(slots.TryEject(uid, "filter2", null, out var removed), Is.True);
            Assert.That(removed, Is.EqualTo(filters[1]));
            Assert.That(refinery.ActiveFilters, Is.EqualTo(2));
            Assert.That(slots.TryInsert(uid, "filter2", removed!.Value, null), Is.True);
        });
        await pair.RunSeconds(3);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<NebulaGasSiphonFilterComponent>(filters[1]).Remaining, Is.EqualTo(2495),
                "Filters must not wear out while idle.");
            var deconstructed = new MachineDeconstructedEvent();
            em.EventBus.RaiseLocalEvent(uid, deconstructed);
            foreach (var filter in filters)
                Assert.That(em.System<SharedContainerSystem>().IsEntityInContainer(filter), Is.False,
                    "Deconstruction must return the cartridges, including depleted ones.");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FullnessUsesOnlyLocalTankAndDiscountIsCappedAfterDowngrading()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 4; x++)
            {
                maps.SetTile(map.Grid, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                em.SpawnEntity("BulkMiningPipe", new EntityCoordinates(map.Grid, x + 0.5f, 0.5f));
            }
            var uid = em.SpawnEntity("BulkMiningRefineryLarge", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var source = em.SpawnEntity("BulkAutoMiningEmitter", new EntityCoordinates(map.Grid, 4.5f, 0.5f));
            em.System<NodeGroupSystem>().ForceUpdate();
            var refinery = em.GetComponent<MiningRefineryComponent>(uid);
            var materials = em.System<SharedMaterialStorageSystem>();
            Assert.That(materials.TryChangeMaterialAmount(source, refinery.SlurryMaterial, 50000), Is.True);
            Assert.That(materials.GetMaterialAmount(uid, refinery.SlurryMaterial), Is.EqualTo(50000));
            Assert.That(refinery.EfficiencyMultiplier, Is.EqualTo(1));
            Assert.That(materials.TryChangeMaterialAmount(uid, refinery.SlurryMaterial, 1500000), Is.True);
            Assert.That(refinery.FullnessDiscount, Is.EqualTo(0.1f).Within(0.00001));

            ReplaceParts(em, uid, "BluespaceMatterBinStockPart", "FemtoManipulatorStockPart");
            var storage = em.GetComponent<MaterialStorageComponent>(uid);
            Assert.That(materials.TryChangeMaterialAmount(uid, refinery.SlurryMaterial,
                storage.StorageLimit!.Value - storage.Storage[refinery.SlurryMaterial]), Is.True);
            ReplaceParts(em, uid, "MatterBinStockPart", "MicroManipulatorStockPart");
            Assert.That(storage.Storage[refinery.SlurryMaterial], Is.GreaterThan(storage.StorageLimit));
            Assert.That(refinery.FullnessDiscount, Is.EqualTo(0.2f).Within(0.00001));
            Assert.That(em.GetComponent<LatheComponent>(uid).FinalMaterialUseMultiplier, Is.EqualTo(0.8f).Within(0.00001));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SavedPartsFiltersAndDiscountsDoNotCompoundOnReload()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var uid = em.SpawnEntity("BulkMiningRefineryLarge", map.GridCoords);
            var refinery = em.GetComponent<MiningRefineryComponent>(uid);
            ReplaceParts(em, uid, "SuperMatterBinStockPart", "PicoManipulatorStockPart");
            var capacity = em.GetComponent<MaterialStorageComponent>(uid).StorageLimit!.Value;
            Assert.That(em.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(uid, refinery.SlurryMaterial, capacity / 2), Is.True);
            var filter = em.SpawnEntity("DrakeRefineryFilter", map.GridCoords);
            var filterComp = em.GetComponent<NebulaGasSiphonFilterComponent>(filter);
            em.System<NebulaGasSiphonSystem>().ConsumeFilter((filter, filterComp), 500);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(uid, "filter3", filter, null), Is.True);
            var expected = em.GetComponent<LatheComponent>(uid).FinalMaterialUseMultiplier;

            var loader = em.System<MapLoaderSystem>();
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid, writer), Is.True);
            em.DeleteEntity(map.Grid);
            using var reader = new StringReader(writer.ToString());
            Assert.That(loader.TryLoadGrid(reader, "large-refinery-save-test", out _, out var grid,
                DeserializationOptions.Default with { InitializeMaps = true }), Is.True);

            var found = 0;
            var query = em.AllEntityQueryEnumerator<MiningRefineryComponent, TransformComponent>();
            while (query.MoveNext(out var loaded, out var comp, out var xform))
            {
                if (xform.GridUid != grid!.Value.Owner)
                    continue;

                found++;
                for (var i = 0; i < 3; i++)
                    em.System<ConstructionSystem>().RefreshParts(loaded, em.GetComponent<MachineComponent>(loaded));
                Assert.That(em.GetComponent<MaterialStorageComponent>(loaded).StorageLimit, Is.EqualTo(capacity));
                Assert.That(comp.Exhaust.Volume, Is.EqualTo(5600).Within(0.001));
                Assert.That(comp.ActiveFilters, Is.EqualTo(1));
                Assert.That(comp.FullnessDiscount, Is.EqualTo(0.1f).Within(0.00001));
                Assert.That(em.GetComponent<LatheComponent>(loaded).FinalMaterialUseMultiplier, Is.EqualTo(expected).Within(0.00001));
                var slot = em.System<SharedContainerSystem>().GetContainer(loaded, "filter3");
                Assert.That(em.GetComponent<NebulaGasSiphonFilterComponent>(slot.ContainedEntities[0]).Remaining, Is.EqualTo(2000));
            }
            Assert.That(found, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LegacySavedRefineryGetsItsExhaustUpgradeWithoutReplacingParts()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var uid = em.SpawnEntity("BulkMiningRefinery", map.GridCoords);
            ReplaceParts(em, uid, "SuperMatterBinStockPart", "PicoManipulatorStockPart");
            var loader = em.System<MapLoaderSystem>();
            using var writer = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid, writer), Is.True);
            var saved = writer.ToString();
            var legacy = _exhaustMultiplierField.Replace(saved, "");
            Assert.That(legacy, Is.Not.EqualTo(saved));
            em.DeleteEntity(map.Grid);
            using var reader = new StringReader(legacy);
            Assert.That(loader.TryLoadGrid(reader, "legacy-refinery-exhaust-test", out _, out var grid,
                DeserializationOptions.Default with { InitializeMaps = true }), Is.True);

            var found = 0;
            var query = em.AllEntityQueryEnumerator<MiningRefineryComponent, TransformComponent>();
            while (query.MoveNext(out var loaded, out var refinery, out var xform))
            {
                if (xform.GridUid != grid!.Value.Owner)
                    continue;

                found++;
                Assert.That(refinery.ExhaustMultiplier, Is.EqualTo(1.12f));
                ProtoId<LatheRecipePrototype> recipeId = "BulkMiningSteelOre";
                var printing = new LatheStartPrintingEvent(pair.Server.ProtoMan.Index(recipeId));
                em.EventBus.RaiseLocalEvent(loaded, ref printing);
                Assert.That(refinery.Exhaust.TotalMoles, Is.EqualTo(11.2f).Within(0.00001));
            }
            Assert.That(found, Is.EqualTo(1));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HyperIsBlockedOnlyOnLargeRefineryAndDrakeFiltersAlsoFitSiphons()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var small = em.SpawnEntity("BulkMiningRefinery", map.GridCoords);
            var large = em.SpawnEntity("BulkMiningRefineryLarge", map.GridCoords);
            var user = em.SpawnEntity("MobObserver", map.GridCoords);
            var hyper = em.SpawnEntity("LatheUpgradeKitHyper", map.GridCoords);
            var cryo = em.SpawnEntity("LatheUpgradeKitCryo", map.GridCoords);
            var upgrades = em.System<UpgradeKitSystem>();
            Assert.That(upgrades.CanUpgrade((hyper, em.GetComponent<UpgradeKitComponent>(hyper)), large, user), Is.False);
            Assert.That(upgrades.CanUpgrade((hyper, em.GetComponent<UpgradeKitComponent>(hyper)), small, user), Is.True);
            Assert.That(upgrades.CanUpgrade((cryo, em.GetComponent<UpgradeKitComponent>(cryo)), large, user), Is.True);
            var siphon = em.SpawnEntity("NebulaGasSiphon", map.GridCoords);
            var filter = em.SpawnEntity("DrakeRefineryFilter", map.GridCoords);
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(siphon, "filter", filter, null), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(0)]
    [TestCase(90)]
    [TestCase(180)]
    [TestCase(270)]
    public async Task LargeExhaustPortConnectsOutsideTheFootprintAfterRotation(int degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        await pair.Server.WaitAssertion(() =>
        {
            var rotation = Angle.FromDegrees(degrees);
            var maps = em.System<SharedMapSystem>();
            for (var y = -4; y <= 0; y++)
            {
                var offset = rotation.RotateVec(new Vector2(0, y));
                maps.SetTile(map.Grid, map.Grid.Comp,
                    new Vector2i((int)MathF.Round(offset.X), (int)MathF.Round(offset.Y)), map.Tile.Tile);
            }
            var uid = em.SpawnEntity("BulkMiningRefineryLarge", new EntityCoordinates(map.Grid, 0.5f, 0.5f));
            var pipe = em.SpawnEntity("GasPipeArmoredStraight",
                new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f) + rotation.RotateVec(new Vector2(0, -3))));
            var outlet = em.SpawnEntity("BulkMiningExhaust",
                new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f) + rotation.RotateVec(new Vector2(0, -4))));
            var transform = em.System<SharedTransformSystem>();
            transform.SetLocalRotation(uid, rotation);
            transform.SetLocalRotation(pipe, rotation);
            transform.SetLocalRotation(outlet, rotation + Angle.FromDegrees(180));
            em.System<NodeGroupSystem>().ForceUpdate();
            var nodes = em.System<NodeContainerSystem>();
            Assert.That(nodes.TryGetNode(uid, "exhaust", out PipeNode refineryNode), Is.True);
            Assert.That(nodes.TryGetNode(outlet, "pipe", out PipeNode outletNode), Is.True);
            Assert.That(refineryNode.NodeGroup, Is.SameAs(outletNode.NodeGroup));
        });
        await pair.CleanReturnAsync();
    }

    private static void ReplaceParts(IEntityManager em, EntityUid uid, string bin, string manipulator)
    {
        var machine = em.GetComponent<MachineComponent>(uid);
        var containers = em.System<SharedContainerSystem>();
        foreach (var part in new List<EntityUid>(machine.PartContainer.ContainedEntities))
        {
            if (!em.TryGetComponent<MachinePartComponent>(part, out var comp))
                continue;

            var prototype = comp.PartType == "MatterBin" ? bin : comp.PartType == "Manipulator" ? manipulator : null;
            if (prototype == null)
                continue;

            em.DeleteEntity(part);
            var replacement = em.SpawnEntity(prototype, em.GetComponent<TransformComponent>(uid).Coordinates);
            Assert.That(containers.Insert(replacement, machine.PartContainer), Is.True);
        }
        em.System<ConstructionSystem>().RefreshParts(uid, machine);
    }
}
