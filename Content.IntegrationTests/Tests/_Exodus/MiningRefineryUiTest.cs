using System.IO;
using System.Linq;
using Content.Client._Exodus.Mining.Pipes.UI;
using Content.Client.Lathe.UI;
using Content.Server.Atmos.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server._Exodus.Mining.Pipes;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface;
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

        await pair.RunSeconds(60);
        await server.WaitAssertion(() =>
        {
            Assert.That(refinery.Exhaust.TotalMoles + gas.Air.TotalMoles, Is.LessThan(35),
                "A connected refinery must discharge the accumulated 70 moles while its UI stays open.");
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
