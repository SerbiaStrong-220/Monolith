using System.Collections.Generic;
using System.Numerics;
using Content.Server._Exodus.Worldgen;
using Content.Shared._Exodus.StarSystem;
using Content.Shared._NF.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(RelativePoiSpawnSystem))]
public sealed class RelativePoiSpawnTest
{
    private static readonly ResPath GridPath = new("/Maps/Test/Breathing/3by3-20oxy-80nit.yml");

    [TestPrototypes]
    private const string Prototypes = """
- type: pointOfInterest
  id: TestRelativePoiA
  name: Test relative A
  gridPath: /Maps/Test/Breathing/3by3-20oxy-80nit.yml
  spawnGroup: TestRelativePois
  spawnChance: 0

- type: pointOfInterest
  id: TestRelativePoiB
  name: Test relative B
  gridPath: /Maps/Test/Breathing/3by3-20oxy-80nit.yml
  spawnGroup: TestRelativePois
  spawnChance: 0

- type: pointOfInterest
  id: TestRelativePoiC
  name: Test relative C
  gridPath: /Maps/Test/Breathing/3by3-20oxy-80nit.yml
  spawnGroup: TestRelativePois
  spawnChance: 0

- type: relativePoiPlacement
  id: TestRelativeA
  poi: TestRelativePoiA
  anchorPoi: TSFMCHalcyon
  minDistance: 3000
  maxDistance: 5000

- type: relativePoiPlacement
  id: TestRelativeB
  poi: TestRelativePoiB
  anchorNebulaPoi: NebulaPoiHokkaido
  minDistance: 3000
  maxDistance: 5000

- type: relativePoiPlacement
  id: TestRelativeC
  poi: TestRelativePoiC
  anchorPoi: TestRelativePoiA
  minDistance: 3000
  maxDistance: 5000

- type: pointOfInterest
  id: TestRelativePlanetPoi
  name: Test planet-relative POI
  gridPath: /Maps/Test/Breathing/3by3-20oxy-80nit.yml
  spawnGroup: TestRelativePois
  spawnChance: 0

- type: relativePoiPlacement
  id: TestRelativePlanet
  poi: TestRelativePlanetPoi
  anchorPlanet: PlanetFervidus
  minDistance: 4000
  maxDistance: 6000
""";

    [Test]
    public async Task PlanetAnchorUsesPositionOnTheSamePausedMapWithoutAnExtraCopy()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var placement = entities.System<RelativePoiSpawnSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var mapUid = maps.CreateMap(out var mapId, runMapInit: false);
            var otherMapUid = maps.CreateMap(out _, runMapInit: false);
            var position = new Vector2(20000, -30000);
            try
            {
                placement.Begin(mapId);
                var output = new List<EntityUid>();
                Assert.That(placement.QueueIfRelative(mapId, new(false, "TestRelativePlanetPoi"), output));

                var wrongMapPlanet = entities.SpawnEntity(null, new EntityCoordinates(otherMapUid, position));
                entities.AddComponent<PlanetMarkerComponent>(wrongMapPlanet).Planet = "PlanetFervidus";
                placement.ProcessPending(mapId);
                Assert.That(output, Is.Empty);

                var planet = entities.SpawnEntity(null, new EntityCoordinates(mapUid, position));
                entities.AddComponent<PlanetMarkerComponent>(planet).Planet = "PlanetFervidus";
                placement.ProcessPending(mapId, final: true);
                Assert.That(output, Has.Count.EqualTo(1));
                var grid = entities.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(output[0]);
                var center = transform.GetWorldMatrix(output[0]).TransformBox(grid.LocalAABB).Center;
                Assert.That(Vector2.Distance(position, center), Is.InRange(3999.99f, 6000.01f));

                placement.ProcessPending(mapId, final: true);
                Assert.That(output, Has.Count.EqualTo(1));
            }
            finally
            {
                entities.DeleteEntity(mapUid);
                entities.DeleteEntity(otherMapUid);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AbsoluteDistanceUsesGridCentersEvenWithSectorScaling()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var config = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var loader = entities.System<MapLoaderSystem>();
            var placement = entities.System<RelativePoiSpawnSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var rule = server.ResolveDependency<IPrototypeManager>().Index<RelativePoiPlacementPrototype>("TestRelativeA");
            var mapUid = maps.CreateMap(out var mapId);
            var oldModifier = config.GetCVar(NFCCVars.POIDistanceModifier);
            try
            {
                config.SetCVar(NFCCVars.POIDistanceModifier, 3f);
                Assert.That(loader.TryLoadGrid(mapId, GridPath, out var anchor, offset: new Vector2(20000, -40000)));
                placement.Register(anchor!.Value.Owner, new(false, "TSFMCHalcyon"), 0);
                var center = transform.GetWorldMatrix(anchor.Value.Owner).TransformBox(anchor.Value.Comp.LocalAABB).Center;

                for (var i = 0; i < 6; i++)
                {
                    Assert.That(placement.TryLoadRelativeGrid(mapId, GridPath, rule, 0, null, out var child));
                    var childCenter = transform.GetWorldMatrix(child!.Value.Owner).TransformBox(child.Value.Comp.LocalAABB).Center;
                    Assert.That(Vector2.Distance(center, childCenter), Is.InRange(2999.99f, 5000.01f));
                    entities.DeleteEntity(child.Value.Owner);
                }
            }
            finally
            {
                config.SetCVar(NFCCVars.POIDistanceModifier, oldModifier);
                entities.DeleteEntity(mapUid);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SatelliteFitsInsideAnchorProtectionWithAnotherNearbyPoi()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var loader = entities.System<MapLoaderSystem>();
            var placement = entities.System<RelativePoiSpawnSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var rule = prototypes.Index<RelativePoiPlacementPrototype>("DamagedArkansawNearCruiseShip");
            var mapUid = maps.CreateMap(out var mapId, runMapInit: false);
            try
            {
                Assert.That(prototypes.Index<RelativePoiPlacementPrototype>("TestRelativeA").IgnoreAnchorClearance, Is.False);
                Assert.That(prototypes.Index<RelativePoiPlacementPrototype>("TestRelativeA").UseGlobalMinimumSeparation, Is.True);
                Assert.That(rule.IgnoreAnchorClearance, Is.True);
                Assert.That(rule.UseGlobalMinimumSeparation, Is.False);
                Assert.That(loader.TryLoadGrid(mapId, GridPath, out var anchor));
                placement.Register(anchor!.Value.Owner, new(true, "NebulaPoiCruiseShip"), 150);
                var anchorBounds = transform.GetWorldMatrix(anchor.Value.Owner).TransformBox(anchor.Value.Comp.LocalAABB);

                Assert.That(loader.TryLoadGrid(mapId, GridPath, out var nearby, offset: new Vector2(1000, 0)));
                placement.Register(nearby!.Value.Owner, new(true, "NebulaPoiBurnedShuttle"), 150);
                var nearbyBounds = transform.GetWorldMatrix(nearby.Value.Owner).TransformBox(nearby.Value.Comp.LocalAABB);

                for (var i = 0; i < 6; i++)
                {
                    Assert.That(placement.TryLoadRelativeGrid(mapId, GridPath, rule, 0, null, out var satellite));
                    var satelliteBounds = transform.GetWorldMatrix(satellite!.Value.Owner)
                        .TransformBox(satellite.Value.Comp.LocalAABB);
                    Assert.That(Vector2.Distance(anchorBounds.Center, satelliteBounds.Center), Is.InRange(69.99f, 100.01f));
                    Assert.That(satelliteBounds.Intersects(anchorBounds), Is.False);
                    Assert.That(Vector2.Distance(nearbyBounds.Center, satelliteBounds.Center), Is.GreaterThanOrEqualTo(150f));
                    Assert.That(satelliteBounds.Intersects(nearbyBounds), Is.False);
                    entities.DeleteEntity(satellite.Value.Owner);
                }
            }
            finally
            {
                entities.DeleteEntity(mapUid);
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RejectedSatelliteStaysOnItsMapUntilDeleted()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid mapUid = default;
        EntityUid? rejectedGrid = null;

        try
        {
            await server.WaitAssertion(() =>
            {
                var maps = entities.System<SharedMapSystem>();
                var loader = entities.System<MapLoaderSystem>();
                var placement = entities.System<RelativePoiSpawnSystem>();
                var rule = server.ResolveDependency<IPrototypeManager>()
                    .Index<RelativePoiPlacementPrototype>("DamagedArkansawNearCruiseShip");
                mapUid = maps.CreateMap(out var mapId, runMapInit: false);
                Assert.That(loader.TryLoadGrid(mapId, GridPath, out var anchor));
                placement.Register(anchor!.Value.Owner, new(true, "NebulaPoiCruiseShip"), 150);

                // Even satellite rules must reject intersections with the target's own clearance.
                var sawmill = server.ResolveDependency<ILogManager>().GetSawmill("system.relative_poi_spawn");
                var previousLevel = sawmill.Level;
                try
                {
                    // Suppress the expected placement failure only; PVS and map errors must still fail the test.
                    sawmill.Level = LogLevel.Fatal;
                    Assert.That(placement.TryLoadRelativeGrid(mapId, GridPath, rule, 1000, null, out var satellite), Is.False);
                    Assert.That(satellite, Is.Null);
                }
                finally
                {
                    sawmill.Level = previousLevel;
                }

                var children = entities.GetComponent<TransformComponent>(mapUid).ChildEnumerator;
                while (children.MoveNext(out var child))
                {
                    if (!entities.IsQueuedForDeletion(child))
                        continue;

                    Assert.That(rejectedGrid, Is.Null, "Only one candidate grid should be loaded across retries.");
                    rejectedGrid = child;
                    Assert.That(entities.GetComponent<TransformComponent>(child).MapUid, Is.EqualTo(mapUid));
                }
                Assert.That(rejectedGrid, Is.Not.Null, "The rejected grid must remain on the map until deletion.");
            });

            await server.WaitRunTicks(2);
            await server.WaitAssertion(() => Assert.That(entities.EntityExists(rejectedGrid!.Value), Is.False));
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (entities.EntityExists(mapUid))
                    entities.DeleteEntity(mapUid);
            });
        }
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeferredChainsWaitForAnchorsOnTheirOwnMapAndSpawnOnlyOnce()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var loader = entities.System<MapLoaderSystem>();
            var placement = entities.System<RelativePoiSpawnSystem>();
            // Generation must also work before MapInit, when grids are paused.
            var mapUid = maps.CreateMap(out var mapId, runMapInit: false);
            var otherMapUid = maps.CreateMap(out var otherMapId, runMapInit: false);
            try
            {
                placement.Begin(mapId);
                var output = new List<EntityUid>();
                Assert.That(placement.QueueIfRelative(mapId, new(false, "TestRelativePoiB"), output));
                Assert.That(placement.QueueIfRelative(mapId, new(false, "TestRelativePoiC"), output));
                Assert.That(placement.QueueIfRelative(mapId, new(false, "TestRelativePoiA"), output));

                Assert.That(loader.TryLoadGrid(otherMapId, GridPath, out var wrongMapAnchor));
                placement.Register(wrongMapAnchor!.Value.Owner, new(false, "TSFMCHalcyon"), 0);
                placement.ProcessPending(mapId);
                Assert.That(output, Is.Empty);

                Assert.That(loader.TryLoadGrid(mapId, GridPath, out var root));
                placement.Register(root!.Value.Owner, new(false, "TSFMCHalcyon"), 0);
                placement.ProcessPending(mapId);
                Assert.That(output.Count, Is.EqualTo(2), "A must spawn before its dependent C regardless of queue order.");

                Assert.That(loader.TryLoadGrid(mapId, GridPath, out var nebulaAnchor, offset: new Vector2(50000, 0)));
                placement.Register(nebulaAnchor!.Value.Owner, new(true, "NebulaPoiHokkaido"), 0);
                placement.ProcessPending(mapId, final: true);
                Assert.That(output.Count, Is.EqualTo(3));
                Assert.That(entities.HasComponent<RelativePoiGenerationComponent>(mapUid), Is.False);
                placement.ProcessPending(mapId, final: true);
                Assert.That(output.Count, Is.EqualTo(3));
            }
            finally
            {
                entities.DeleteEntity(mapUid);
                entities.DeleteEntity(otherMapUid);
            }
        });
        await pair.CleanReturnAsync();
    }
}
