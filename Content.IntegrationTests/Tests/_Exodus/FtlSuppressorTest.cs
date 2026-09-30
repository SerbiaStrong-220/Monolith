using System.Numerics;
using Content.Server._Exodus.Shuttles.Components;
using Content.Server._Exodus.Shuttles.Systems;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(FtlSuppressorSystem))]
public sealed class FtlSuppressorTest
{
    [TestCase("MachineFtlSuppressorTsf", 1000f)]
    [TestCase("MachineFtlSuppressorPdv", 750f)]
    public async Task ActiveSuppressorBlocksJumpsInsideItsField(string prototype, float range)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var loc = server.ResolveDependency<ILocalizationManager>();
        var map = await pair.CreateTestMap();
        var mapManager = server.ResolveDependency<IMapManager>();
        var suppressors = em.System<FtlSuppressorSystem>();
        var shuttles = em.System<ShuttleSystem>();
        var consoles = em.System<ShuttleConsoleSystem>();
        var power = em.System<PowerReceiverSystem>();
        var xforms = em.System<SharedTransformSystem>();
        var inside = new Vector2(range - 60f, 0f);
        var outside = new Vector2(range + 60f, 0f);
        EntityUid suppressor = default;
        EntityUid ship = default;

        await server.WaitAssertion(() =>
        {
            suppressor = em.SpawnEntity(prototype, new EntityCoordinates(map.Grid, .5f, .5f));
            power.SetNeedsPower(suppressor, false);
            ship = CreateShip(em, mapManager, map.MapId, map.Tile.Tile, inside);
        });
        await PoolManager.WaitUntil(server, () => em.HasComponent<ActiveFtlSuppressorComponent>(suppressor));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<FtlSuppressorComponent>(suppressor).Range, Is.EqualTo(range));

            // Leaving the field is blocked for everyone, including the grid carrying the suppressor.
            Assert.That(shuttles.CanFTL(ship, out var reason), Is.False);
            Assert.That(reason, Is.EqualTo(loc.GetString(FtlSuppressorSystem.LeaveRejection)));
            Assert.That(shuttles.CanFTL(map.Grid, out _), Is.False);

            // Arriving into the field is blocked, arriving right outside of it is not.
            Assert.That(suppressors.CanFTLTo(new EntityCoordinates(map.MapUid, inside), out var rejection), Is.False);
            Assert.That(rejection, Is.EqualTo(FtlSuppressorSystem.TargetRejection));
            Assert.That(suppressors.CanFTLTo(new EntityCoordinates(map.MapUid, outside), out _), Is.True);

            // The field is published to shuttle consoles as an exclusion zone.
            Assert.That(HasZone(consoles, ship, range), Is.True);

            xforms.SetWorldPosition(ship, outside);
            Assert.That(shuttles.CanFTL(ship, out _), Is.True);
            xforms.SetWorldPosition(ship, inside);
            Assert.That(shuttles.CanFTL(ship, out _), Is.False);

            // Switching the machine off through its power switch drops the field.
            power.SetPowerDisabled(suppressor, true);
        });
        await PoolManager.WaitUntil(server, () => !em.HasComponent<ActiveFtlSuppressorComponent>(suppressor));
        await server.WaitAssertion(() =>
        {
            Assert.That(shuttles.CanFTL(ship, out _), Is.True);
            Assert.That(suppressors.CanFTLTo(new EntityCoordinates(map.MapUid, inside), out _), Is.True);
            Assert.That(HasZone(consoles, ship, range), Is.False);
            power.SetPowerDisabled(suppressor, false);
        });
        await PoolManager.WaitUntil(server, () => em.HasComponent<ActiveFtlSuppressorComponent>(suppressor));
        await server.WaitAssertion(() =>
        {
            Assert.That(shuttles.CanFTL(ship, out _), Is.False);

            // An unanchored suppressor does not project a field even while powered.
            xforms.Unanchor(suppressor, em.GetComponent<TransformComponent>(suppressor));
            Assert.That(em.HasComponent<ActiveFtlSuppressorComponent>(suppressor), Is.False);
            Assert.That(shuttles.CanFTL(ship, out _), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WeakerSuppressorLeavesGapCoveredByStrongerOne()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var mapManager = server.ResolveDependency<IMapManager>();
        var shuttles = em.System<ShuttleSystem>();
        var power = em.System<PowerReceiverSystem>();
        EntityUid pdv = default;
        EntityUid ship = default;

        await server.WaitAssertion(() =>
        {
            pdv = em.SpawnEntity("MachineFtlSuppressorPdv", new EntityCoordinates(map.Grid, .5f, .5f));
            power.SetNeedsPower(pdv, false);
            ship = CreateShip(em, mapManager, map.MapId, map.Tile.Tile, new Vector2(850f, 0f));
        });
        await PoolManager.WaitUntil(server, () => em.HasComponent<ActiveFtlSuppressorComponent>(pdv));
        await server.WaitAssertion(() =>
        {
            // 850 m is outside the 750 m DF field...
            Assert.That(shuttles.CanFTL(ship, out _), Is.True);
            em.DeleteEntity(pdv);
            var tsf = em.SpawnEntity("MachineFtlSuppressorTsf", new EntityCoordinates(map.Grid, .5f, .5f));
            power.SetNeedsPower(tsf, false);
        });
        await PoolManager.WaitUntil(server, () => em.Count<ActiveFtlSuppressorComponent>() > 0);
        await server.WaitAssertion(() =>
        {
            // ...but inside the 1 km TSF field.
            Assert.That(shuttles.CanFTL(ship, out _), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpoolingConsoleJumpIsAbortedByField()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var mapManager = server.ResolveDependency<IMapManager>();
        var suppressors = em.System<FtlSuppressorSystem>();
        var shuttles = em.System<ShuttleSystem>();
        var power = em.System<PowerReceiverSystem>();
        var xforms = em.System<SharedTransformSystem>();
        EntityUid suppressor = default;
        EntityUid ship = default;

        await server.WaitAssertion(() =>
        {
            suppressor = em.SpawnEntity("MachineFtlSuppressorTsf", new EntityCoordinates(map.Grid, .5f, .5f));
            power.SetNeedsPower(suppressor, false);
            power.SetPowerDisabled(suppressor, true);
            ship = CreateShip(em, mapManager, map.MapId, map.Tile.Tile, new Vector2(500f, 0f));
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<ActiveFtlSuppressorComponent>(suppressor), Is.False);

            // The shuttle starts spooling inside the future field.
            StartSpool(em, shuttles, suppressors, ship, new EntityCoordinates(map.MapUid, 5000f, 0f));
            power.SetPowerDisabled(suppressor, false);
        });
        await PoolManager.WaitUntil(server, () => !em.HasComponent<FTLComponent>(ship));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.HasComponent<FtlSuppressionSpoolComponent>(ship), Is.False);
            Assert.That(em.GetComponent<TransformComponent>(ship).MapID, Is.EqualTo(map.MapId));

            // A shuttle outside of the field spooling towards it is stopped as well.
            xforms.SetWorldPosition(ship, new Vector2(3000f, 0f));
            StartSpool(em, shuttles, suppressors, ship, new EntityCoordinates(map.MapUid, 400f, 0f));
        });
        await PoolManager.WaitUntil(server, () => !em.HasComponent<FTLComponent>(ship));
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(ship).MapID, Is.EqualTo(map.MapId));

            // Jumps that neither start nor end in a field keep spooling.
            StartSpool(em, shuttles, suppressors, ship, new EntityCoordinates(map.MapUid, 6000f, 0f));
        });
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.TryGetComponent<FTLComponent>(ship, out var ftl), Is.True);
            Assert.That(ftl!.State, Is.EqualTo(FTLState.Starting));
            Assert.That(em.HasComponent<FtlSuppressionSpoolComponent>(ship), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    private static void StartSpool(
        IEntityManager em,
        ShuttleSystem shuttles,
        FtlSuppressorSystem suppressors,
        EntityUid ship,
        EntityCoordinates target)
    {
        shuttles.FTLToCoordinates(ship, em.GetComponent<ShuttleComponent>(ship), target, Angle.Zero, 120f, 30f);
        suppressors.TrackConsoleSpool(ship);
        Assert.That(em.GetComponent<FTLComponent>(ship).State, Is.EqualTo(FTLState.Starting));
        Assert.That(em.HasComponent<FtlSuppressionSpoolComponent>(ship), Is.True);
    }

    private static bool HasZone(ShuttleConsoleSystem consoles, EntityUid shuttle, float range)
    {
        foreach (var exclusion in consoles.GetMapState(shuttle).Exclusions)
        {
            if (MathHelper.CloseTo(exclusion.Range, range))
                return true;
        }

        return false;
    }

    private static EntityUid CreateShip(IEntityManager em, IMapManager mapManager, MapId mapId, Tile tile, Vector2 position)
    {
        var grid = mapManager.CreateGridEntity(mapId);
        em.System<SharedMapSystem>().SetTile(grid, grid.Comp, Vector2i.Zero, tile);
        em.System<SharedTransformSystem>().SetWorldPosition(grid, position);
        return grid;
    }
}
