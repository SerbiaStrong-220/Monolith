using System.Numerics;
using Content.Server._Exodus.Nebula.Hazards;
using Content.Server._Exodus.Shuttles.Components;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Exodus.Shuttles;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class PhasicThrusterTest
{
    [TestCase(0)]
    [TestCase(90)]
    [TestCase(180)]
    [TestCase(270)]
    public async Task CornerNozzlesRotateFireAndBlockIndependently(int degrees)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid engine = default;
        var rotation = Angle.FromDegrees(degrees);
        var first = degrees / 90;
        var second = (first + 1) % 4;
        var north = rotation.RotateVec(Vector2.UnitY);
        var block = new Vector2i((int) MathF.Round(north.X), (int) MathF.Round(north.Y));

        await server.WaitAssertion(() =>
        {
            engine = em.SpawnEntity("NebulaThrusterCorner", new EntityCoordinates(map.Grid, .5f, .5f));
            em.System<SharedTransformSystem>().SetLocalRotation(engine, rotation);
            em.System<PowerReceiverSystem>().SetNeedsPower(engine, false);
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ThrusterComponent>(engine).IsOn);
        await server.WaitAssertion(() =>
        {
            var shuttle = em.GetComponent<ShuttleComponent>(map.Grid);
            var system = em.System<ThrusterSystem>();
            var thruster = em.GetComponent<ThrusterComponent>(engine);
            var nozzles = em.GetComponent<ThrusterNozzlesComponent>(engine);
            var appearance = em.System<SharedAppearanceSystem>();
            AssertDirections(shuttle, engine, 100, first, second);
            system.EnableLinearThrustDirection(shuttle, (DirectionFlag) (1 << first));
            Assert.That(nozzles.FiringNozzles, Is.EqualTo(DirectionFlag.South));
            Assert.That(appearance.TryGetData<bool>(engine, ThrusterNozzleDirection.South, out var lit) && lit, Is.True);
            system.EnableLinearThrustDirection(shuttle, (DirectionFlag) (1 << second));
            Assert.That(nozzles.FiringNozzles, Is.EqualTo(DirectionFlag.South | DirectionFlag.East));
            system.DisableLinearThrustDirection(shuttle, (DirectionFlag) (1 << first));
            Assert.That(thruster.Firing, Is.True, "Stopping one nozzle must not stop the other.");
            Assert.That(nozzles.FiringNozzles, Is.EqualTo(DirectionFlag.East));
            system.DisableLinearThrusters(shuttle);

            var nebula = em.System<NebulaShuttleThrustSystem>();
            Assert.That(nebula.GetEffectiveDirectionThrust(map.Grid, first, 100, .1f, true), Is.EqualTo(400));
            Assert.That(nebula.GetEffectiveDirectionThrust(map.Grid, second, 100, .1f, true), Is.EqualTo(400));
            em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, block, map.Tile.Tile);
            AssertDirections(shuttle, engine, 100, second);
            em.System<SharedMapSystem>().SetTile(map.Grid.Owner, map.Grid.Comp, block, Tile.Empty);
            AssertDirections(shuttle, engine, 100, first, second);

            system.SetEnabled((engine, thruster), false);
            AssertDirections(shuttle, engine, 0);
            system.SetEnabled((engine, thruster), true);
            AssertDirections(shuttle, engine, 100, first, second);
            var parts = new RefreshPartsEvent();
            parts.PartRatings[thruster.MachinePartThrust] = 3;
            em.EventBus.RaiseLocalEvent(engine, parts);
            AssertDirections(shuttle, engine, 156.25f, first, second);
            em.EventBus.RaiseLocalEvent(engine, parts);
            AssertDirections(shuttle, engine, 156.25f, first, second);

            em.System<SharedTransformSystem>().SetLocalRotation(engine, rotation + Angle.FromDegrees(90));
            AssertDirections(shuttle, engine, 156.25f, second, (second + 1) % 4);
            em.DeleteEntity(engine);
            AssertDirections(shuttle, engine, 0);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LargeNozzleRequiresSpaceAcrossItsWholeWidthAndRecoversAfterPowerLoss()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid engine = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(-1, 0), map.Tile.Tile);
            maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
            engine = em.SpawnEntity("NebulaThrusterLarge", new EntityCoordinates(map.Grid, .5f, .5f));
            em.System<PowerReceiverSystem>().SetNeedsPower(engine, false);
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ThrusterComponent>(engine).IsOn);
        await server.WaitAssertion(() =>
        {
            var shuttle = em.GetComponent<ShuttleComponent>(map.Grid);
            AssertDirections(shuttle, engine, 500, 0);
            var nebula = em.System<NebulaShuttleThrustSystem>();
            Assert.That(nebula.GetEffectiveDirectionThrust(map.Grid, 0, 500, .1f, true), Is.EqualTo(2000));
            var maps = em.System<SharedMapSystem>();
            for (var x = -1; x <= 1; x++)
            {
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 1), map.Tile.Tile);
                AssertDirections(shuttle, engine, 0);
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 1), Tile.Empty);
                AssertDirections(shuttle, engine, 500, 0);
            }
            em.System<PowerReceiverSystem>().SetNeedsPower(engine, true);
        });
        await PoolManager.WaitUntil(server, () => !em.GetComponent<ThrusterComponent>(engine).IsOn);
        await server.WaitAssertion(() =>
        {
            AssertDirections(em.GetComponent<ShuttleComponent>(map.Grid), engine, 0);
            em.System<PowerReceiverSystem>().SetNeedsPower(engine, false);
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ThrusterComponent>(engine).IsOn);
        await server.WaitAssertion(() =>
        {
            var shuttle = em.GetComponent<ShuttleComponent>(map.Grid);
            AssertDirections(shuttle, engine, 500, 0);
            var xform = em.GetComponent<TransformComponent>(engine);
            em.System<SharedTransformSystem>().Unanchor(engine, xform);
            AssertDirections(shuttle, engine, 0);
            Assert.That(em.GetComponent<ThrusterNozzlesComponent>(engine).WatchedGrid, Is.Null);
            em.System<SharedTransformSystem>().AnchorEntity((engine, xform));
            AssertDirections(shuttle, engine, 500, 0);
            em.DeleteEntity(engine);
            AssertDirections(shuttle, engine, 0);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("NebulaThruster", 100f, false)]
    [TestCase("VacuumGradientThruster", 2500f, true)]
    public async Task ExistingThrustersKeepTheirDirections(string prototype, float thrust, bool omni)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid engine = default;
        await server.WaitAssertion(() =>
        {
            engine = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
            em.System<PowerReceiverSystem>().SetNeedsPower(engine, false);
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ThrusterComponent>(engine).IsOn);
        await server.WaitAssertion(() =>
        {
            var shuttle = em.GetComponent<ShuttleComponent>(map.Grid);
            AssertDirections(shuttle, engine, thrust, omni ? [0, 1, 2, 3] : [0]);
            em.DeleteEntity(engine);
            AssertDirections(shuttle, engine, 0);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OnlyFiringNozzlesSupplyTargetsToTheExistingBurnSystem()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid engine = default;
        EntityUid north = default;
        EntityUid west = default;
        await server.WaitAssertion(() =>
        {
            engine = em.SpawnEntity("NebulaThrusterCorner", new EntityCoordinates(map.Grid, .5f, .5f));
            north = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, .5f, 1.4f));
            west = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, -.4f, .5f));
            em.System<PowerReceiverSystem>().SetNeedsPower(engine, false);
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ThrusterComponent>(engine).IsOn);
        await pair.RunTicksSync(4);
        await server.WaitAssertion(() =>
        {
            var shuttle = em.GetComponent<ShuttleComponent>(map.Grid);
            var thruster = em.GetComponent<ThrusterComponent>(engine);
            var system = em.System<ThrusterSystem>();
            system.EnableLinearThrustDirection(shuttle, DirectionFlag.South);
            Assert.That(thruster.Colliding, Does.Contain(north));
            Assert.That(thruster.Colliding, Does.Not.Contain(west));
            system.EnableLinearThrustDirection(shuttle, DirectionFlag.East);
            Assert.That(thruster.Colliding, Does.Contain(north));
            Assert.That(thruster.Colliding, Does.Contain(west));
            system.DisableLinearThrustDirection(shuttle, DirectionFlag.South);
            Assert.That(thruster.Colliding, Does.Not.Contain(north));
            Assert.That(thruster.Colliding, Does.Contain(west));
            system.DisableLinearThrusters(shuttle);
            Assert.That(thruster.Colliding, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task LargeBoardUsesTheExistingFrameSizeRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var coords = new EntityCoordinates(map.Grid, .5f, .5f);
            var user = em.SpawnEntity("MobHuman", coords);
            var board = em.SpawnEntity("NebulaThrusterLargeMachineCircuitboard", coords);
            var small = em.SpawnEntity("MachineFrame", coords);
            var frame = em.SpawnEntity("MachineFrame3x1", coords);
            em.EventBus.RaiseLocalEvent(small, new InteractUsingEvent(user, board, small, coords));
            Assert.That(em.GetComponent<MachineFrameComponent>(small).BoardContainer.ContainedEntities, Is.Empty);
            em.EventBus.RaiseLocalEvent(frame, new InteractUsingEvent(user, board, frame, coords));
            var machine = em.GetComponent<MachineFrameComponent>(frame);
            Assert.That(machine.BoardContainer.ContainedEntities, Does.Contain(board));
            Assert.That(machine.Requirements["Capacitor"], Is.EqualTo(3));
            Assert.That(machine.MaterialRequirements["Steel"], Is.EqualTo(15));
        });
        await pair.CleanReturnAsync();
    }

    private static void AssertDirections(ShuttleComponent shuttle, EntityUid engine, float thrust, params int[] directions)
    {
        for (var i = 0; i < 4; i++)
        {
            var present = Array.IndexOf(directions, i) >= 0;
            Assert.That(shuttle.LinearThrusters[i].FindAll(uid => uid == engine).Count, Is.EqualTo(present ? 1 : 0), $"Direction {i}");
            Assert.That(shuttle.LinearThrust[i], Is.EqualTo(present ? thrust : 0).Within(.001f), $"Thrust {i}");
        }
    }
}
