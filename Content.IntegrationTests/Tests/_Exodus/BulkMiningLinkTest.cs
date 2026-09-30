using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._Exodus.Mining.AutoMining;
using Content.IntegrationTests.Pair;
using Content.Server._Exodus.Mining.AutoMining;
using Content.Server._Exodus.Mining.Pipes;
using Content.Server._Exodus.Mining.Pipes.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

// Arrange link checks directly to exercise each break path deterministically.
#pragma warning disable RA0002

[TestFixture]
public sealed class BulkMiningLinkTest
{
    [Test]
    public void ConsortiumBonusHasDiminishingReturns()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BulkMiningLinkBonus.Get(1, 0.15f, 0.5f, 0.3f), Is.Zero);
            Assert.That(BulkMiningLinkBonus.Get(2, 0.15f, 0.5f, 0.3f), Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(BulkMiningLinkBonus.Get(3, 0.15f, 0.5f, 0.3f), Is.EqualTo(0.225f).Within(0.0001f));
            Assert.That(BulkMiningLinkBonus.Get(4, 0.15f, 0.5f, 0.3f), Is.EqualTo(0.2625f).Within(0.0001f));
            Assert.That(BulkMiningLinkBonus.Get(40, 0.15f, 0.5f, 0.3f), Is.LessThanOrEqualTo(0.3f));
            Assert.That(BulkMiningLinkBonus.Get(3, 0.15f, 0.5f, 0.2f), Is.EqualTo(0.2f).Within(0.0001f), "The cap applies.");
            Assert.That(BulkMiningLinkBonus.Get(3, 0.15f, float.NaN, 1f), Is.EqualTo(0.15f).Within(0.0001f));
        });
    }

    [Test]
    public async Task RequestAndAcceptJoinLiquidMetalNetworksAndApplyBonuses()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        LinkShip first = default!;
        LinkShip second = default!;
        EntityUid refinery = default;
        await pair.Server.WaitAssertion(() =>
        {
            first = CreateShip(pair, map, new Vector2(0, 30), 2.5f);
            second = CreateShip(pair, map, new Vector2(20, 30), -2.5f);
            // The first ship feeds a refinery from its linked laser through an ore duct.
            refinery = em.SpawnEntity("BulkMiningRefinery", new EntityCoordinates(first.Grid, -1.5f, -3.5f));
            for (var y = -4; y <= 0; y++)
                em.SpawnEntity("BulkMiningPipe", new EntityCoordinates(first.Grid, -1.5f, y + .5f));

            for (var x = -1; x <= 2; x++)
                em.SpawnEntity("BulkMiningPipe", new EntityCoordinates(first.Grid, x + .5f, .5f));

            em.System<NodeGroupSystem>().ForceUpdate();
        });
        await WaitPowered(pair, first, second);

        await pair.Server.WaitAssertion(() =>
        {
            var system = em.System<BulkAutoMiningSystem>();
            var pipes = em.System<MiningPipeNetSystem>();
            var materials = em.System<SharedMaterialStorageSystem>();
            var refineries = em.System<MiningRefinerySystem>();
            var member = em.GetComponent<MiningPipeNetworkMemberComponent>(refinery);
            var lathe = em.GetComponent<LatheComponent>(refinery);
            var slurry = em.GetComponent<MiningRefineryComponent>(refinery).SlurryMaterial;
            var baseTime = lathe.FinalTimeMultiplier;
            var baseMaterial = lathe.FinalMaterialUseMultiplier;
            Assert.That(materials.TryChangeMaterialAmount(second.Laser, slurry, 5000, localOnly: true), Is.True);

            // Networks stay separate until both ships consent.
            Assert.That(pipes.FillBuffer((refinery, member), slurry), Is.Zero);
            Assert.That(system.TryRequestLink(first.Console, second.Grid), Is.True);
            Assert.That(em.GetComponent<BulkAutoMiningEmitterComponent>(first.Laser).LinkPartner, Is.Null);
            Assert.That(system.TryAcceptLink(first.Console, second.Grid), Is.False, "Only the asked ship can accept.");
            Assert.That(system.TryAcceptLink(second.Console, first.Grid), Is.True);

            var firstLaser = em.GetComponent<BulkAutoMiningEmitterComponent>(first.Laser);
            var secondLaser = em.GetComponent<BulkAutoMiningEmitterComponent>(second.Laser);
            Assert.Multiple(() =>
            {
                Assert.That(firstLaser.LinkPartner, Is.EqualTo(second.Laser));
                Assert.That(secondLaser.LinkPartner, Is.EqualTo(first.Laser));
                Assert.That(firstLaser.LinkGrid, Is.EqualTo(second.Grid.Owner));
                Assert.That(firstLaser.BeamGrid, Is.Null, "A linked laser cannot mine.");
                Assert.That(em.GetComponent<MiningPipeBridgeComponent>(first.Laser).Partner, Is.EqualTo(second.Laser));
                Assert.That(system.GetConsortiumSize(first.Grid), Is.EqualTo(2));
            });

            // The first ship's refinery now draws the second ship's slurry.
            Assert.That(pipes.FillBuffer((refinery, member), slurry), Is.EqualTo(5000));
            Assert.That(materials.GetMaterialAmount(second.Laser, slurry, localOnly: true), Is.Zero);
            Assert.That(pipes.CountJoinedShips((refinery, member)), Is.EqualTo(2));
            Assert.That(materials.TryChangeMaterialAmount(second.Laser, slurry, 700, localOnly: true), Is.True);
            Assert.That(materials.GetMaterialAmount(refinery, slurry), Is.EqualTo(5700), "Remote buffers are offered to the lathe.");

            refineries.UpdateLinkBonus((refinery, em.GetComponent<MiningRefineryComponent>(refinery)), 2);
            Assert.That(lathe.FinalTimeMultiplier, Is.EqualTo(baseTime / 1.15f).Within(0.0001f));
            Assert.That(lathe.FinalMaterialUseMultiplier, Is.EqualTo(baseMaterial / 1.15f).Within(0.0001f));
            Assert.That(em.GetComponent<MiningRefineryComponent>(refinery).StorageState.LinkBonus, Is.EqualTo(0.15f).Within(0.0001f));

            Assert.That(system.TryBreakLink(second.Console, first.Grid), Is.True, "Either side may break the link.");
            Assert.That(firstLaser.LinkPartner, Is.Null);
            Assert.That(secondLaser.LinkPartner, Is.Null);
            Assert.That(em.HasComponent<MiningPipeBridgeComponent>(first.Laser), Is.False);
            Assert.That(pipes.FillBuffer((refinery, member), slurry), Is.Zero);
            Assert.That(pipes.CountJoinedShips((refinery, member)), Is.EqualTo(1));

            refineries.UpdateLinkBonus((refinery, em.GetComponent<MiningRefineryComponent>(refinery)), 1);
            Assert.That(lathe.FinalTimeMultiplier, Is.EqualTo(baseTime).Within(0.0001f), "Removing the bonus must not drift.");
            Assert.That(lathe.FinalMaterialUseMultiplier, Is.EqualTo(baseMaterial).Within(0.0001f));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RequestsNeedConsentAndCrossingRequestsLink()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        LinkShip first = default!;
        LinkShip second = default!;
        await pair.Server.WaitAssertion(() =>
        {
            first = CreateShip(pair, map, new Vector2(0, 30), 2.5f);
            second = CreateShip(pair, map, new Vector2(20, 30), -2.5f);
        });
        await WaitPowered(pair, first, second);

        await pair.Server.WaitAssertion(() =>
        {
            var system = em.System<BulkAutoMiningSystem>();
            Assert.That(system.TryRequestLink(first.Console, second.Grid), Is.True);
            Assert.That(system.TryRequestLink(first.Console, second.Grid), Is.False, "A pending request is not repeated.");
            Assert.That(system.DeclineLink(second.Console, first.Grid), Is.True);
            Assert.That(system.TryAcceptLink(second.Console, first.Grid), Is.False, "A declined request cannot be accepted.");
            Assert.That(em.GetComponent<BulkAutoMiningEmitterComponent>(first.Laser).LinkPartner, Is.Null);

            // The second ship asks back: its request crosses the first ship's consent.
            Assert.That(system.TryRequestLink(second.Console, first.Grid), Is.True);
            first.Console.Comp.LinkRequestCooldown = TimeSpan.Zero;
            em.GetComponent<BulkMiningLinkGridComponent>(first.Grid).NextRequestTime = TimeSpan.Zero;
            Assert.That(system.TryRequestLink(first.Console, second.Grid), Is.True);
            Assert.That(em.GetComponent<BulkAutoMiningEmitterComponent>(first.Laser).LinkPartner, Is.EqualTo(second.Laser));
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(Obstacle.OwnShield, false)]
    [TestCase(Obstacle.PartnerShield, false)]
    [TestCase(Obstacle.ForeignShield, true)]
    [TestCase(Obstacle.Wall, true)]
    [TestCase(Obstacle.Range, true)]
    public async Task LinkBreaksOnlyForForeignObstaclesAndRange(Obstacle obstacle, bool breaks)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        LinkShip first = default!;
        LinkShip second = default!;
        await pair.Server.WaitAssertion(() =>
        {
            first = CreateShip(pair, map, new Vector2(0, 30), 2.5f);
            second = CreateShip(pair, map, new Vector2(20, 30), -2.5f);
        });
        await WaitPowered(pair, first, second);

        await pair.Server.WaitAssertion(() =>
        {
            var system = em.System<BulkAutoMiningSystem>();
            Assert.That(system.TryRequestLink(first.Console, second.Grid), Is.True);
            Assert.That(system.TryAcceptLink(second.Console, first.Grid), Is.True);

            // A third grid between the lasers, clear of both ships' hulls.
            var third = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
            em.System<SharedTransformSystem>().SetWorldPosition(third, new Vector2(10, 30));
            em.System<SharedMapSystem>().SetTile(third, third.Comp, Vector2i.Zero, map.Tile.Tile);
            var between = new EntityCoordinates(third, .5f, .5f);
            switch (obstacle)
            {
                case Obstacle.Range:
                    em.System<SharedTransformSystem>().SetWorldPosition(second.Grid, new Vector2(400, 30));
                    break;
                case Obstacle.Wall:
                    em.SpawnEntity("WallSolid", between);
                    break;
                default:
                    var shield = em.SpawnEntity("WallSolid", between);
                    var shielded = obstacle == Obstacle.OwnShield ? first.Grid.Owner
                        : obstacle == Obstacle.PartnerShield ? second.Grid.Owner
                        : third.Owner;
                    em.AddComponent<ShipShieldComponent>(shield).Shielded = shielded;
                    break;
            }

            // A single obstructed check is tolerated; the second one breaks the link.
            for (var i = 0; i < 2; i++)
            {
                foreach (var laser in new[] { first.Laser, second.Laser })
                    em.GetComponent<BulkAutoMiningEmitterComponent>(laser).NextLinkCheck = TimeSpan.Zero;

                system.Update(0);
            }

            var linked = em.GetComponent<BulkAutoMiningEmitterComponent>(first.Laser).LinkPartner != null;
            Assert.That(linked, Is.EqualTo(!breaks));
            if (!breaks)
                return;

            var expected = obstacle == Obstacle.Range
                ? BulkMiningLinkBreakReason.Range
                : BulkMiningLinkBreakReason.Obstructed;
            Assert.That(em.GetComponent<BulkAutoMiningEmitterComponent>(first.Laser).LastLinkBreak, Is.EqualTo(expected));
            Assert.That(em.GetComponent<BulkAutoMiningEmitterComponent>(second.Laser).LastLinkBreak, Is.EqualTo(expected));
            Assert.That(em.GetComponent<BulkAutoMiningEmitterComponent>(second.Laser).LinkPartner, Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ChainedLinksFormOneConsortium()
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        LinkShip first = default!;
        LinkShip middle = default!;
        LinkShip last = default!;
        await pair.Server.WaitAssertion(() =>
        {
            first = CreateShip(pair, map, new Vector2(0, 30), 2.5f);
            middle = CreateShip(pair, map, new Vector2(20, 30), -2.5f, 2.5f);
            last = CreateShip(pair, map, new Vector2(40, 30), -2.5f);
        });
        await WaitPowered(pair, first, middle, last);

        await pair.Server.WaitAssertion(() =>
        {
            var system = em.System<BulkAutoMiningSystem>();
            Assert.That(system.TryRequestLink(first.Console, middle.Grid), Is.True);
            Assert.That(system.TryAcceptLink(middle.Console, first.Grid), Is.True);
            Assert.That(system.TryRequestLink(last.Console, middle.Grid), Is.True);
            Assert.That(system.TryAcceptLink(middle.Console, last.Grid), Is.True);

            Assert.That(system.GetConsortiumSize(first.Grid), Is.EqualTo(3));
            Assert.That(system.GetConsortiumSize(last.Grid), Is.EqualTo(3));
            Assert.That(system.GetConsortiumBonus(system.GetConsortiumSize(first.Grid)), Is.EqualTo(0.225f).Within(0.0001f));

            // Each link occupies its own laser on the middle ship; no laser is left for another link.
            Assert.That(system.TryRequestLink(middle.Console, map.Grid), Is.False);
            Assert.That(system.TryBreakLink(first.Console, middle.Grid), Is.True);
            Assert.That(system.GetConsortiumSize(last.Grid), Is.EqualTo(2));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConsoleWindowShowsIncomingRequestsAndLinks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        LinkShip first = default!;
        LinkShip second = default!;
        EntityUid actor = default;
        await server.WaitAssertion(() =>
        {
            first = CreateShip(pair, map, new Vector2(0, 30), 2.5f);
            second = CreateShip(pair, map, new Vector2(20, 30), -2.5f);
        });
        await WaitPowered(pair, first, second);
        await server.WaitAssertion(() =>
        {
            actor = em.SpawnEntity("MobObserver", new EntityCoordinates(first.Grid, -2.5f, -4.5f));
            server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
        });
        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.System<SharedUserInterfaceSystem>().TryOpenUi(first.Console.Owner, BulkAutoMiningUiKey.Key, actor), Is.True);
            Assert.That(em.System<BulkAutoMiningSystem>().TryRequestLink(second.Console, first.Grid), Is.True);
        });
        await pair.RunTicksSync(10);

        BulkAutoMiningWindow window = null;
        await client.WaitAssertion(() =>
        {
            window = client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<BulkAutoMiningWindow>().Single();
            window.SetLinkMode(true);
            Assert.That(window.LinkMode, Is.True);
            Assert.That(window.RadarControl.LinkMode, Is.True);
            Assert.That(window.LinkPanelControl.IncomingRequests, Is.EqualTo(1));
        });

        await server.WaitAssertion(() =>
            Assert.That(em.System<BulkAutoMiningSystem>().TryAcceptLink(first.Console, second.Grid), Is.True));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(window.LinkPanelControl.ActiveLinks, Is.EqualTo(1));
            Assert.That(window.LinkPanelControl.IncomingRequests, Is.Zero);
            window.SetLinkMode(false);
            Assert.That(window.RadarControl.LinkMode, Is.False);
        });
        await pair.CleanReturnAsync();
    }

    public enum Obstacle : byte
    {
        OwnShield,
        PartnerShield,
        ForeignShield,
        Wall,
        Range,
    }

    /// <summary>A small ship with a console and lasers at the given local X positions, facing along the X axis.</summary>
    private static LinkShip CreateShip(TestPair pair, TestMapData map, Vector2 position, params float[] lasers)
    {
        var em = pair.Server.EntMan;
        var maps = em.System<SharedMapSystem>();
        var grid = pair.Server.ResolveDependency<IMapManager>().CreateGridEntity(map.MapId);
        em.System<SharedTransformSystem>().SetWorldPosition(grid, position);
        for (var x = -4; x <= 3; x++)
        {
            for (var y = -5; y <= 2; y++)
                maps.SetTile(grid, grid.Comp, new Vector2i(x, y), map.Tile.Tile);
        }

        var power = em.System<PowerReceiverSystem>();
        var consoleUid = em.SpawnEntity("BulkAutoMiningConsole", new EntityCoordinates(grid, -3.5f, -4.5f));
        var console = em.GetComponent<BulkAutoMiningConsoleComponent>(consoleUid);
        console.MaxRange = 64;
        power.SetNeedsPower(consoleUid, false);
        var ship = new LinkShip { Grid = grid, Console = (consoleUid, console) };
        foreach (var x in lasers)
        {
            var laser = em.SpawnEntity("BulkAutoMiningEmitter", new EntityCoordinates(grid, x, .5f));
            power.SetNeedsPower(laser, false);
            ship.Lasers.Add(laser);
        }

        return ship;
    }

    private static async Task WaitPowered(TestPair pair, params LinkShip[] ships)
    {
        var em = pair.Server.EntMan;
        await PoolManager.WaitUntil(pair.Server, () =>
        {
            foreach (var ship in ships)
            {
                if (!em.GetComponent<ApcPowerReceiverComponent>(ship.Console).Powered)
                    return false;

                foreach (var laser in ship.Lasers)
                {
                    if (!em.GetComponent<ApcPowerReceiverComponent>(laser).Powered)
                        return false;
                }
            }

            return true;
        });
    }

    private sealed class LinkShip
    {
        public Entity<MapGridComponent> Grid;
        public Entity<BulkAutoMiningConsoleComponent> Console;
        public readonly List<EntityUid> Lasers = new();
        public EntityUid Laser => Lasers[0];
    }
}

#pragma warning restore RA0002
