using System.Collections.Generic;
using System.Numerics;
using Content.Server._Exodus.Territory;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Server._Mono.Radar;
using Content.Server.Store.Systems;
using Content.Shared._Exodus.Store.Components;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task RootedCoresOverrideFlagsOnceAndRestoreControlAfterTheLastCoreLeaves()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x < 12; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);

            var territory = em.AddComponent<GridTerritoryComponent>(map.Grid);
            var territories = em.System<GridTerritorySystem>();
            var counter = em.System<TerritoryCounterSystem>();
            var initialScore = counter.GetScore("Rot");
            var flag = em.SpawnEntity("BannerNGC", new EntityCoordinates(map.Grid, .5f, .5f));
            Assert.That(em.System<SharedTransformSystem>().AnchorEntity((flag, em.GetComponent<TransformComponent>(flag))), Is.True);
            territories.SetController(map.Grid, "TSFMC", flag);

            var stores = new List<(EntityUid Store, float OriginalDiscount)>();
            foreach (var faction in new[] { "TSFMC", "PDV", "Khsira", "Syndicate" })
            {
                var store = em.SpawnEntity(null, map.GridCoords);
                em.AddComponent(store, new TerritoryStoreDiscountComponent { Faction = faction });
                var data = new GetStoreUiDataEvent();
                em.EventBus.RaiseLocalEvent(store, ref data);
                stores.Add((store, data.PriceMultiplier));
            }

            var first = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, 4.5f, .5f));
            var second = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, 8.5f, .5f));
            Assert.That(territory.ControllingFaction?.Id, Is.EqualTo("Rot"));
            Assert.That(counter.GetScore("Rot"), Is.EqualTo(initialScore + 3));
            Assert.That(territories.TrySetCorporateController(map.Grid, "Colonial"), Is.False);

            territories.SetController(map.Grid, "TSFMC", flag);
            territories.ClearController(map.Grid);
            Assert.That(territory.ControllingFaction?.Id, Is.EqualTo("Rot"), "Ordinary claims cannot displace a rooted core.");

            // Each opted-in uplink faction receives the penalty, including the otherwise negative Syndicate.
            foreach (var (store, originalDiscount) in stores)
            {
                var data = new GetStoreUiDataEvent();
                em.EventBus.RaiseLocalEvent(store, ref data);
                Assert.That(data.HasPriceModifier, Is.True);
                Assert.That(data.PriceMultiplier, Is.LessThan(originalDiscount));
            }

            em.System<MobStateSystem>().ChangeMobState(first, MobState.Dead);
            Assert.That(territory.ControllingFaction?.Id, Is.EqualTo("Rot"));
            Assert.That(territory.ActiveClaimBanner, Is.EqualTo(second));
            Assert.That(counter.GetScore("Rot"), Is.EqualTo(initialScore + 3));

            em.System<SharedTransformSystem>().Unanchor(second);
            Assert.That(territory.ControllingFaction?.Id, Is.EqualTo("TSFMC"));
            Assert.That(territory.ActiveClaimBanner, Is.EqualTo(flag));
            Assert.That(counter.GetScore("Rot"), Is.EqualTo(initialScore));
            Assert.That(em.HasComponent<RadarBlipComponent>(flag), Is.True, "Restoring the flag also restores its radar marker.");
            Assert.That(em.GetComponent<RadarBlipComponent>(second).Config.Bounds.Size, Is.EqualTo(new Vector2(3f, 3f)),
                "The core keeps its own radar marker when it loses territory.");
            foreach (var (store, originalDiscount) in stores)
            {
                var data = new GetStoreUiDataEvent();
                em.EventBus.RaiseLocalEvent(store, ref data);
                Assert.That(data.PriceMultiplier, Is.EqualTo(originalDiscount).Within(.0001f));
            }
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DestroyedOrUnanchoredFlagsCannotRegainControlAfterRotLeaves(bool destroyFlag)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            // Keep the flag and core on one connected grid; isolated tiles trigger grid splitting.
            for (var x = 1; x <= 4; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            var territory = em.AddComponent<GridTerritoryComponent>(map.Grid);
            var territories = em.System<GridTerritorySystem>();
            var flag = em.SpawnEntity("BannerNGC", map.GridCoords);
            Assert.That(em.System<SharedTransformSystem>().AnchorEntity((flag, em.GetComponent<TransformComponent>(flag))), Is.True);
            territories.SetController(map.Grid, "TSFMC", flag);
            var core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, 4.5f, .5f));
            if (destroyFlag)
                em.DeleteEntity(flag);
            else
                em.System<SharedTransformSystem>().Unanchor(flag);
            Assert.That(territory.ControllingFaction?.Id, Is.EqualTo("Rot"));

            em.DeleteEntity(core);
            Assert.That(territory.ControllingFaction, Is.Null);
            Assert.That(territory.ActiveClaimBanner, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InterruptedCaptureRestartsWithoutGrantingInstantFlagOwnership()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            // Keep the flag and core on one connected grid; isolated tiles trigger grid splitting.
            for (var x = 1; x <= 4; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            var territory = em.AddComponent<GridTerritoryComponent>(map.Grid);
            var territories = em.System<GridTerritorySystem>();
            var flag = em.SpawnEntity("BannerNGC", map.GridCoords);
            Assert.That(em.System<SharedTransformSystem>().AnchorEntity((flag, em.GetComponent<TransformComponent>(flag))), Is.True);
            territories.ClearController(map.Grid);
            var banner = em.GetComponent<TerritoryBannerComponent>(flag);
            Assert.That(em.GetComponent<TransformComponent>(flag).GridUid, Is.EqualTo(map.Grid.Owner));
            Assert.That(territories.TryStartCapture((map.Grid, territory), (flag, banner), null), Is.True);

            var core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, 4.5f, .5f));
            var capture = em.GetComponent<TerritoryCaptureComponent>(map.Grid);
            Assert.That(capture.Faction, Is.Null);
            Assert.That(territory.ControllingFaction?.Id, Is.EqualTo("Rot"));
            em.DeleteEntity(core);
            Assert.That(territory.ControllingFaction, Is.Null);
            Assert.That(capture.Faction?.Id, Is.EqualTo("TSFMC"));
            Assert.That(capture.EndsAt, Is.GreaterThan(server.ResolveDependency<IGameTiming>().CurTime));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UprootingMovesTheClaimAndProtectedGridsRemainUnclaimed()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var destination = await pair.CreateTestMap();
        EntityUid core = default;
        await server.WaitAssertion(() =>
        {
            em.AddComponent<GridTerritoryComponent>(map.Grid);
            var protectedTerritory = em.AddComponent<GridTerritoryComponent>(destination.Grid);
            protectedTerritory.Claimable = false;
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 4; x++)
                maps.SetTile(destination.Grid.Owner, destination.Grid.Comp, new Vector2i(x, 0), destination.Tile.Tile);
            var protectedCore = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(destination.Grid, 4.5f, .5f));
            Assert.That(protectedTerritory.ControllingFaction, Is.Null);
            em.DeleteEntity(protectedCore);
            protectedTerritory.Claimable = true;
            core = em.SpawnEntity("MobRotIntelligent", map.GridCoords);
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            brain.RootDuration = TimeSpan.FromSeconds(.1);
            Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, brain)), Is.True);
            Assert.That(em.GetComponent<GridTerritoryComponent>(map.Grid).ControllingFaction?.Id, Is.EqualTo("Rot"),
                "Starting the uprooting do-after does not release a still-anchored core's territory.");
        });
        await pair.RunSeconds(.4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<GridTerritoryComponent>(map.Grid).ControllingFaction, Is.Null);
            em.System<SharedTransformSystem>().SetCoordinates(core, destination.GridCoords);
            Assert.That(em.GetComponent<GridTerritoryComponent>(destination.Grid).ControllingFaction, Is.Null);
            Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, em.GetComponent<RotIntelligentComponent>(core))), Is.True);
        });
        await pair.RunSeconds(.4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<GridTerritoryComponent>(destination.Grid).ControllingFaction?.Id, Is.EqualTo("Rot"));
            var counter = em.System<TerritoryCounterSystem>();
            var before = counter.GetScore("Rot");
            em.DeleteEntity(destination.Grid);
            Assert.That(counter.GetScore("Rot"), Is.EqualTo(before - 3));
            em.DeleteEntity(map.MapUid);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TerritoryAddedAfterTheCoreStillAppliesItsClaim()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            // CreateTestMap initializes the map, but CreateGridEntity does not map-initialize its grid.
            em.RunMapInit(map.Grid.Owner, em.GetComponent<MetaDataComponent>(map.Grid));
            var core = em.SpawnEntity("MobRotIntelligent", map.GridCoords);
            var territory = em.AddComponent<GridTerritoryComponent>(map.Grid);
            Assert.That(territory.ControllingFaction?.Id, Is.EqualTo("Rot"));
            Assert.That(territory.ActiveClaimBanner, Is.EqualTo(core));
            em.DeleteEntity(core);
            Assert.That(territory.ControllingFaction, Is.Null);
        });
        await pair.CleanReturnAsync();
    }
}
