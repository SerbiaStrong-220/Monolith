using System.IO;
using System.Linq;
using Content.Client._Exodus.Mining.Pipes.UI;
using Content.Client.Lathe.UI;
using Content.Server.Atmos.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server._Exodus.Mining.Pipes;
using Content.Server._Exodus.Nebula;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared._Exodus.Nebula;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class MiningRefineryUiTest
{
    [Test]
    public async Task LargeRefinerySynchronizesFilterSocketsAndDiscountsWhileWindowIsOpen()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server;
        var client = pair.Client;
        var em = server.EntMan;
        EntityUid uid = default;
        EntityUid actor = default;
        MiningRefineryStorageControl control = null;
        await server.WaitAssertion(() =>
        {
            uid = em.SpawnEntity("BulkMiningRefineryLarge", map.GridCoords);
            var refinery = em.GetComponent<MiningRefineryComponent>(uid);
            var slots = em.System<ItemSlotsSystem>();
            for (var i = 0; i < refinery.FilterSlots.Count; i++)
            {
                var filter = em.SpawnEntity("DrakeRefineryFilter", map.GridCoords);
                var comp = em.GetComponent<NebulaGasSiphonFilterComponent>(filter);
                if (i == 0)
                    em.System<NebulaGasSiphonSystem>().ConsumeFilter((filter, comp), comp.Remaining - 5);
                Assert.That(slots.TryInsert(uid, refinery.FilterSlots[i], filter, null), Is.True);
            }
            Assert.That(em.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(uid, refinery.SlurryMaterial, 1500000), Is.True);
            actor = em.SpawnEntity("MobObserver", new EntityCoordinates(map.Grid, 3.5f, 0.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() => Assert.That(em.System<SharedUserInterfaceSystem>().TryOpenUi(uid, LatheUiKey.Key, actor), Is.True));
        await pair.RunUntilSynced();
        await client.WaitAssertion(() =>
        {
            var local = pair.ToClientUid(uid);
            var comp = client.EntMan.GetComponent<MiningRefineryComponent>(local);
            var sprite = client.EntMan.GetComponent<SpriteComponent>(local);
            var menu = client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<LatheMenu>().Single();
            control = menu.StatusContainer.Children.OfType<MiningRefineryStorageControl>().Single();
            Assert.That(control.FindControl<PanelContainer>("EfficiencyPanel").Visible, Is.True);
            Assert.That(comp.StorageState.FullnessDiscount, Is.EqualTo(0.1f).Within(0.00001));
            Assert.That(comp.StorageState.FilterDiscount, Is.EqualTo(0.2f).Within(0.00001));
            foreach (var slot in comp.FilterSlots)
            {
                Assert.That(sprite[slot].Visible, Is.True);
                Assert.That(sprite[slot].RsiState.ToString(), Is.EqualTo("mounted"));
            }
        });

        await server.WaitAssertion(() =>
        {
            ProtoId<LatheRecipePrototype> recipeId = "BulkMiningSteelOre";
            var printing = new LatheStartPrintingEvent(server.ProtoMan.Index(recipeId));
            em.EventBus.RaiseLocalEvent(uid, ref printing);
            Assert.That(em.System<ItemSlotsSystem>().TryEject(uid, "filter2", null, out _), Is.True);
        });
        await pair.RunTicksSync(5);
        await pair.RunUntilSynced();
        await client.WaitAssertion(() =>
        {
            var local = pair.ToClientUid(uid);
            var comp = client.EntMan.GetComponent<MiningRefineryComponent>(local);
            var sprite = client.EntMan.GetComponent<SpriteComponent>(local);
            Assert.That(client.EntMan.System<SharedUserInterfaceSystem>().IsUiOpen(local, LatheUiKey.Key), Is.True);
            Assert.That(comp.StorageState.ActiveFilters, Is.EqualTo(2));
            Assert.That(comp.StorageState.InstalledFilters, Is.EqualTo(3));
            Assert.That(comp.StorageState.FilterDiscount, Is.EqualTo(0.1f).Within(0.00001));
            Assert.That(sprite["filter1"].Visible, Is.True);
            Assert.That(sprite["filter1"].RsiState.ToString(), Is.EqualTo("mounted-depleted"));
            Assert.That(sprite["filter2"].Visible, Is.False);
            Assert.That(control.FindControl<PanelContainer>("EfficiencyPanel").Visible, Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OpenWindowTracksRemoteMaterialsAndExhaustWithoutReopening(bool reload)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var gridUid = map.Grid.Owner;
        EntityUid refineryUid = default;
        EntityUid sourceUid = default;
        EntityUid actor = default;
        MiningRefineryComponent refinery = null;
        MiningRefineryStorageControl control = null;
        PipeNode gas = null;

        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 4; x++)
            {
                for (var y = -3; y <= 1; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            }
            em.EnsureComponent<GridAtmosphereComponent>(map.Grid);
            refineryUid = em.SpawnEntity("BulkMiningRefinery", new EntityCoordinates(map.Grid, .5f, .5f));
            sourceUid = em.SpawnEntity("BulkAutoMiningEmitter", new EntityCoordinates(map.Grid, 4.5f, .5f));
            for (var x = 0; x <= 4; x++)
                em.SpawnEntity("BulkMiningPipe", new EntityCoordinates(map.Grid, x + .5f, .5f));
            em.SpawnEntity("GasPipeArmoredStraight", new EntityCoordinates(map.Grid, .5f, -1.5f));
            var exhaust = em.SpawnEntity("BulkMiningExhaust", new EntityCoordinates(map.Grid, .5f, -2.5f));
            em.System<SharedTransformSystem>().SetLocalRotation(exhaust, Angle.FromDegrees(180));
            em.System<NodeGroupSystem>().ForceUpdate();

            refinery = em.GetComponent<MiningRefineryComponent>(refineryUid);
            Assert.That(em.System<NodeContainerSystem>().TryGetNode(refineryUid, refinery.ExhaustNode, out gas), Is.True);
            ProtoId<LatheRecipePrototype> recipeId = "BulkMiningSteelOre";
            var printing = new LatheStartPrintingEvent(server.ProtoMan.Index(recipeId));
            for (var i = 0; i < 7; i++)
                em.EventBus.RaiseLocalEvent(refineryUid, ref printing);
            Assert.That(refinery.Exhaust.TotalMoles, Is.EqualTo(70));
        });

        if (reload)
        {
            string saved = null;
            await server.WaitAssertion(() =>
            {
                using var writer = new StringWriter();
                Assert.That(em.System<MapLoaderSystem>().TrySaveGrid(gridUid, writer), Is.True);
                saved = writer.ToString();
                em.DeleteEntity(gridUid);
            });
            await pair.RunSeconds(60);
            await server.WaitAssertion(() =>
            {
                using var reader = new StringReader(saved);
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "mining-refinery-ui", out var grid,
                    DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
                gridUid = grid!.Value.Owner;
                var members = em.AllEntityQueryEnumerator<MiningPipeNetworkMemberComponent, TransformComponent>();
                while (members.MoveNext(out var uid, out var member, out var xform))
                {
                    if (xform.GridUid != gridUid)
                        continue;

                    if (em.TryGetComponent(uid, out MiningRefineryComponent comp))
                    {
                        refineryUid = uid;
                        refinery = comp;
                    }
                    else if (member.SupplyMaterials)
                        sourceUid = uid;
                }
                em.System<NodeGroupSystem>().ForceUpdate();
                Assert.That(em.System<NodeContainerSystem>().TryGetNode(refineryUid, refinery.ExhaustNode, out gas), Is.True);
            });
        }

        await server.WaitAssertion(() =>
        {
            actor = em.SpawnEntity("MobObserver", new EntityCoordinates(gridUid, 1.5f, .5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.System<SharedUserInterfaceSystem>().TryOpenUi(refineryUid, LatheUiKey.Key, actor), Is.True);
        });
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            var uid = pair.ToClientUid(refineryUid);
            Assert.That(client.EntMan.System<SharedUserInterfaceSystem>()
                .TryGetOpenUi<MiningRefineryBoundUserInterface>(uid, LatheUiKey.Key, out _), Is.True);
            var menu = client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<LatheMenu>().Single();
            control = menu.StatusContainer.Children.OfType<MiningRefineryStorageControl>().Single();
        });

        await server.WaitAssertion(() =>
        {
            var materials = em.System<SharedMaterialStorageSystem>();
            var capacity = em.GetComponent<MaterialStorageComponent>(refineryUid).StorageLimit!.Value;
            Assert.That(materials.TryChangeMaterialAmount(refineryUid, refinery.SlurryMaterial, capacity), Is.True);
            Assert.That(materials.TryChangeMaterialAmount(sourceUid, refinery.SlurryMaterial, 10000), Is.True);
        });
        await pair.RunTicksSync(5);
        await AssertReadings();

        await server.WaitAssertion(() =>
        {
            Assert.That(em.System<SharedMaterialStorageSystem>()
                .TryChangeMaterialAmount(sourceUid, refinery.SlurryMaterial, -5000), Is.True);
        });
        await pair.RunTicksSync(5);
        await AssertReadings();

        await server.WaitAssertion(() =>
        {
            var storage = em.GetComponent<MaterialStorageComponent>(sourceUid);
            em.System<SharedMaterialStorageSystem>().SetStorageLimit((sourceUid, storage), storage.StorageLimit * 2);
        });
        await pair.RunTicksSync(5);
        await AssertReadings();

        await server.WaitAssertion(() =>
        {
            em.System<SharedTransformSystem>().Unanchor(sourceUid);
            em.System<NodeGroupSystem>().ForceUpdate();
        });
        await pair.RunTicksSync(5);
        await AssertReadings();

        await server.WaitAssertion(() =>
        {
            Assert.That(em.System<SharedTransformSystem>().AnchorEntity(sourceUid), Is.True);
            em.System<NodeGroupSystem>().ForceUpdate();
        });
        await pair.RunTicksSync(5);
        await AssertReadings();

        var gasBeforeVenting = 0f;
        await server.WaitAssertion(() => gasBeforeVenting = refinery.Exhaust.TotalMoles + gas.Air.TotalMoles);
        await pair.RunSeconds(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(refinery.Exhaust.TotalMoles + gas.Air.TotalMoles, Is.LessThan(gasBeforeVenting),
                "A connected refinery must discharge accumulated gas while its UI stays open.");
        });
        await AssertReadings();
        await pair.CleanReturnAsync();

        async Task AssertReadings()
        {
            MiningRefineryStorageState expected = default;
            await server.WaitAssertion(() =>
            {
                Assert.That(em.System<SharedUserInterfaceSystem>().IsUiOpen(refineryUid, LatheUiKey.Key), Is.True);
                expected = refinery.StorageState;
                Assert.That(expected.SlurryStored,
                    Is.EqualTo(em.System<SharedMaterialStorageSystem>().GetMaterialAmount(refineryUid, refinery.SlurryMaterial)),
                    $"Readings must update while open. Time: {server.Timing.CurTime}; next update: {refinery.NextUpdate}; " +
                    $"paused: {em.GetComponent<MetaDataComponent>(refineryUid).EntityPaused}; exhaust: {refinery.Exhaust.TotalMoles}; pipe: {gas.Air.TotalMoles}.");
                Assert.That(expected.GasMoles, Is.EqualTo(refinery.Exhaust.TotalMoles));
                Assert.That(expected.SlurryCapacity, Is.EqualTo(em.System<MiningPipeNetSystem>()
                    .GetStorageCapacity((refineryUid, em.GetComponent<MiningPipeNetworkMemberComponent>(refineryUid)))));
            });
            await pair.RunUntilSynced();
            await client.WaitAssertion(() =>
            {
                var comp = client.EntMan.GetComponent<MiningRefineryComponent>(pair.ToClientUid(refineryUid));
                Assert.That(comp.StorageState.SlurryStored, Is.EqualTo(expected.SlurryStored));
                Assert.That(control.SlurryBar.Value,
                    Is.EqualTo(expected.SlurryStored / (float)expected.SlurryCapacity!.Value).Within(0.0001f));
                Assert.That(control.GasBar.Value,
                    Is.EqualTo(comp.StorageState.GasMoles / comp.ExplosionThreshold).Within(0.0001f));
            });
        }
    }
}
