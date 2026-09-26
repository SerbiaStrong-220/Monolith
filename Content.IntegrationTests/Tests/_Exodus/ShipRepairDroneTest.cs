using System.Numerics;
using Content.Server._Exodus.ShipRepair;
using Content.Server.Power.Components;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Damage;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(ShipRepairDroneSystem))]
public sealed class ShipRepairDroneTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task SurvivingWallDoesNotPreventFloorRepairOrHealing(bool healOnly)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var repair = entities.System<SharedShipRepairSystem>();
            var drones = entities.System<ShipRepairDroneSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var map = maps.CreateMap(out var mapId);
            try
            {
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                for (var x = -3; x <= 2; x++)
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, y), new Tile(1));

                var wall = entities.SpawnEntity("WallSolid", new EntityCoordinates(grid, new Vector2(0.5f, 0.5f)));
                var wallTransform = entities.GetComponent<TransformComponent>(wall);
                if (!wallTransform.Anchored)
                    transform.AnchorEntity((wall, wallTransform));
                Assert.That(wallTransform.Anchored, Is.True);
                repair.GenerateRepairData(grid);
                var data = entities.GetComponent<ShipRepairDataComponent>(grid);
                Assert.That(data.EntityPalette, Has.Count.EqualTo(1));
                // Changing a nonempty floor keeps the wall anchored, as in the reported floorPending cases.
                maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(2));
                entities.System<DamageableSystem>().TryChangeDamage(wall,
                    new DamageSpecifier { DamageDict = { ["Blunt"] = 10 } }, true);

                // Spawn the station after taking the snapshot so it is not a repair candidate.
                var stationUid = entities.SpawnEntity("ShipRepairDroneStation",
                    new EntityCoordinates(grid, new Vector2(-1.5f, 0.5f)));
                var stationTransform = entities.GetComponent<TransformComponent>(stationUid);
                if (!stationTransform.Anchored)
                    transform.AnchorEntity((stationUid, stationTransform));
                var station = entities.GetComponent<ShipRepairStationComponent>(stationUid);
                var receiver = entities.GetComponent<ApcPowerReceiverComponent>(stationUid);
                receiver.PowerDisabled = false;
                receiver.Powered = true;
                station.LastGrid = grid;

                var droneUid = entities.SpawnEntity("MobShipRepairDroneAsakim",
                    new EntityCoordinates(grid, new Vector2(-0.5f, 0.5f)));
                var drone = entities.GetComponent<ShipRepairDroneComponent>(droneUid);
                var tool = entities.GetComponent<ShipRepairToolComponent>(droneUid);
                tool.EnableTileRepair = !healOnly;
                drone.Station = stationUid;
                drone.Grid = grid;
                drone.Revision = data.Revision;
                drone.Command = ShipRepairDroneCommand.Repair;
                drone.Enabled = true;
                drone.NextSearch = timing.CurTime;
                drone.NextUpdate = timing.CurTime;
                station.Drones.Add(droneUid);
                entities.EnsureComponent<ShipRepairWorkQueueComponent>(grid).Drones.Add(droneUid);

                drones.Update(0f);

                Assert.That(drone.Plan, Is.Not.Null, "The drone must select useful work instead of failing its approach.");
                Assert.That(drone.Plan!.Work, Has.Count.EqualTo(1));
                var work = drone.Plan.Work[0];
                Assert.That(work.Operation, Is.EqualTo(healOnly ? ShipRepairOperation.Heal : ShipRepairOperation.Tile));
                Assert.That(work.Target.Tile, Is.EqualTo(Vector2i.Zero));
                Assert.That(drone.FailedTargets, Is.Empty);
                if (healOnly)
                    Assert.That(work.Original, Is.EqualTo(wall));
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });

        await pair.CleanReturnAsync();
    }
}
