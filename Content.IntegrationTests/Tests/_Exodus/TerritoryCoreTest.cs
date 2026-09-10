using System.Numerics;
using System.Linq;
using Content.IntegrationTests.Pair;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server._Exodus.Territory;
using Content.Shared._Exodus.Shuttles;
using Content.Shared._Exodus.Territory;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(TerritoryCoreSystem))]
public sealed class TerritoryCoreTest
{
    [Test]
    public async Task CoreSpawnsIndependentGhostRolesOnNearestFreeBiomass()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var territories = entities.System<GridTerritorySystem>();
            var cores = entities.System<TerritoryCoreSystem>();
            var counter = entities.System<TerritoryCounterSystem>();
            var timing = server.ResolveDependency<IGameTiming>();
            var map = maps.CreateMap(out var mapId);
            var initialScore = counter.GetScore("Chimera");
            try
            {
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                for (var x = 0; x < 8; x++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), new Tile(1));

                var territory = entities.AddComponent<GridTerritoryComponent>(grid.Owner);
                territories.SetController(grid.Owner, "TSFMC");
                Assert.That(territories.TrySetCorporateController(grid.Owner, "Colonial"), Is.True);

                var coreUid = entities.SpawnEntity("ExodusChimeraCore", At(0));
                var core = entities.GetComponent<TerritoryCoreComponent>(coreUid);
                Assert.That(core.SpawnPrototype.Id, Is.EqualTo("MobLetoferolHorrorGhostrole"));
                Assert.That(core.SpawnInterval, Is.EqualTo(TimeSpan.FromMinutes(7)));
                Assert.That(cores.IsActive(coreUid), Is.False, "An unanchored core must not provide benefits.");

                // The organic claim replaces the faction, and no mapped/static corporate claim may survive.
                territories.ClearController(grid.Owner);
                Assert.That(transform.AnchorEntity((coreUid, entities.GetComponent<TransformComponent>(coreUid))), Is.True);
                Assert.That(territory.ControllingFaction?.Id, Is.EqualTo("Chimera"));
                Assert.That(territory.CorporateController, Is.Null);
                Assert.That(territories.TrySetCorporateController(grid.Owner, "Colonial"), Is.False);
                Assert.That(cores.IsActive(coreUid), Is.True);
                Assert.That(counter.GetScore("Chimera"), Is.GreaterThan(initialScore));

                cores.Update(0f);
                Assert.That(SpawnedCreatures(), Is.Empty, "The first creature must wait seven minutes.");

                // No coating: retry later, without spawning on bare floor.
                ForceSpawnCheck();
                Assert.That(SpawnedCreatures(), Is.Empty);
                Assert.That(core.NextCheck, Is.GreaterThan(timing.CurTime));

                for (var x = 1; x <= 6; x++)
                    entities.SpawnEntity("ChimeraFleshKudzu", At(x));
                entities.SpawnEntity("WallSolid", At(1));

                ForceSpawnCheck();
                var first = SpawnedCreatures().Single();
                var creatureTransform = entities.GetComponent<TransformComponent>(first);
                Assert.That(creatureTransform.Anchored, Is.False);
                Assert.That(creatureTransform.GridUid, Is.EqualTo(grid.Owner));
                Assert.That(entities.GetComponent<MetaDataComponent>(first).EntityPrototype?.ID, Is.EqualTo(core.SpawnPrototype.Id));
                Assert.That(entities.GetComponent<GhostRoleComponent>(first).RaffleConfig, Is.Not.Null);
                Assert.That(maps.TileIndicesFor(grid.Owner, grid.Comp, creatureTransform.Coordinates), Is.EqualTo(new Vector2i(2, 0)),
                    "The nearer coated tile is occupied by a wall.");

                // Existing creatures do not impose a population or unoccupied-role cap.
                for (var i = 0; i < 3; i++)
                    ForceSpawnCheck();
                Assert.That(SpawnedCreatures(), Has.Length.EqualTo(4));
                Assert.That(core.NextSpawn, Is.EqualTo(timing.CurTime + TimeSpan.FromMinutes(7)));

                var spawned = SpawnedCreatures();
                territories.ClearController(grid.Owner);
                Assert.That(core.ActiveGrid, Is.Null);
                ForceSpawnCheck();
                Assert.That(SpawnedCreatures(), Is.EquivalentTo(spawned), "Losing control must stop spawning.");

                entities.DeleteEntity(coreUid);
                Assert.That(territory.ControllingFaction, Is.Null);
                Assert.That(counter.GetScore("Chimera"), Is.EqualTo(initialScore));
                foreach (var creature in spawned)
                {
                    Assert.That(entities.EntityExists(creature), Is.True);
                    Assert.That(entities.IsQueuedForDeletion(creature), Is.False);
                    Assert.That(entities.HasComponent<GhostTakeoverAvailableComponent>(creature), Is.True);
                }

                cores.Update(0f);
                Assert.That(SpawnedCreatures(), Is.EquivalentTo(spawned));

                EntityUid[] SpawnedCreatures() => entities.System<GhostRoleSystem>().GhostRoles
                    .Where(role => entities.GetComponent<TransformComponent>(role.Owner).GridUid == grid.Owner &&
                                   entities.HasComponent<GhostTakeoverAvailableComponent>(role.Owner))
                    .Select(role => role.Owner)
                    .ToArray();

                void ForceSpawnCheck()
                {
                    core.NextCheck = timing.CurTime;
                    core.NextSpawn = timing.CurTime;
                    cores.Update(0f);
                }

                EntityCoordinates At(int x) => new(grid.Owner, new Vector2(x + 0.5f, 0.5f));
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpawnedCreatureCanBeRaffledAfterCoreDestruction()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Dirty = true,
            DummyTicker = false,
            Connected = true,
        });
        var server = pair.Server;
        var entities = server.EntMan;
        var mapData = await pair.CreateTestMap();
        var session = server.ResolveDependency<Robust.Server.Player.IPlayerManager>().Sessions.Single();
        var originalMind = session.ContentData()!.Mind!.Value;

        await server.WaitPost(() =>
        {
            var originalBody = entities.SpawnEntity(null, mapData.GridCoords);
            entities.System<SharedMindSystem>().TransferTo(originalMind, originalBody, true);
        });
        await pair.RunTicksSync(10);
        pair.Client.ResolveDependency<IConsoleHost>().ExecuteCommand("ghost");
        await pair.RunTicksSync(10);

        EntityUid creature = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<GhostComponent>(session.AttachedEntity), Is.True);
            var maps = entities.System<SharedMapSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var gridUid = transform.GetGrid(mapData.GridCoords)!.Value;
            var corePosition = mapData.GridCoords;
            var floorPosition = new EntityCoordinates(corePosition.EntityId, corePosition.Position + Vector2.UnitX);
            maps.SetTile((gridUid, entities.GetComponent<MapGridComponent>(gridUid)), floorPosition, new Tile(1));
            entities.AddComponent<GridTerritoryComponent>(gridUid);
            var coreUid = entities.SpawnEntity("ExodusChimeraCore", corePosition);
            transform.AnchorEntity((coreUid, entities.GetComponent<TransformComponent>(coreUid)));
            entities.SpawnEntity("ChimeraFleshKudzu", floorPosition);
            var core = entities.GetComponent<TerritoryCoreComponent>(coreUid);
            core.NextCheck = core.NextSpawn = server.ResolveDependency<IGameTiming>().CurTime;
            entities.System<TerritoryCoreSystem>().Update(0f);

            var roles = entities.System<GhostRoleSystem>();
            var role = roles.GhostRoles.Single(candidate =>
                entities.GetComponent<TransformComponent>(candidate.Owner).GridUid == gridUid &&
                entities.HasComponent<GhostTakeoverAvailableComponent>(candidate.Owner));
            creature = role.Owner;
            entities.DeleteEntity(coreUid);

            // Exercise the normal raffle and actual mind transfer, not just a validation event.
            // Use the supported short lottery in this dirty test; the system owns the countdown.
            server.CfgMan.SetCVar(CCVars.GhostQuickLottery, true);
            roles.Request(session, role.Comp.Identifier);
            var raffle = entities.GetComponent<GhostRoleRaffleComponent>(creature);
            Assert.That(raffle.CurrentMembers, Does.Contain(session));
        });
        await pair.RunSeconds(2);
        await server.WaitAssertion(() => Assert.That(session.AttachedEntity, Is.EqualTo(creature)));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RegenerationOnlyModifiesNaturalHealingOnTheControlledGrid()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var map = maps.CreateMap(out var mapId);
            try
            {
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                for (var x = 0; x < 3; x++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), new Tile(1));
                entities.AddComponent<GridTerritoryComponent>(grid.Owner);

                var core = entities.SpawnEntity("ExodusChimeraCore", At(0));
                transform.AnchorEntity((core, entities.GetComponent<TransformComponent>(core)));

                var creature = entities.SpawnEntity(null, At(1));
                entities.AddComponent<TerritoryRegenerationComponent>(creature).Faction = "Chimera";
                var source = new DamageSpecifier
                {
                    DamageDict = { ["Slash"] = FixedPoint2.New(-4), ["Heat"] = FixedPoint2.New(2) },
                };
                var pulse = new ModifyPassiveDamageEvent(source);
                entities.EventBus.RaiseLocalEvent(creature, ref pulse);
                Assert.That(pulse.Damage.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(-5)));
                Assert.That(pulse.Damage.DamageDict["Heat"], Is.EqualTo(FixedPoint2.New(2)));
                Assert.That(source.DamageDict["Slash"], Is.EqualTo(FixedPoint2.New(-4)), "Never mutate base regeneration.");

                var unmarked = entities.SpawnEntity(null, At(2));
                pulse = new ModifyPassiveDamageEvent(source);
                entities.EventBus.RaiseLocalEvent(unmarked, ref pulse);
                Assert.That(pulse.Damage, Is.SameAs(source));

                // Another grid, even on the same map, does not inherit the station's healing aura.
                var otherGrid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                maps.SetTile(otherGrid.Owner, otherGrid.Comp, Vector2i.Zero, new Tile(1));
                transform.SetCoordinates(creature, new EntityCoordinates(otherGrid.Owner, new Vector2(0.5f, 0.5f)));
                pulse = new ModifyPassiveDamageEvent(source);
                entities.EventBus.RaiseLocalEvent(creature, ref pulse);
                Assert.That(pulse.Damage, Is.SameAs(source));

                transform.SetCoordinates(creature, At(1));
                entities.DeleteEntity(core);
                pulse = new ModifyPassiveDamageEvent(source);
                entities.EventBus.RaiseLocalEvent(creature, ref pulse);
                Assert.That(pulse.Damage, Is.SameAs(source));

                EntityCoordinates At(int x) => new(grid.Owner, new Vector2(x + 0.5f, 0.5f));
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeletingTheGridReleasesItsInfestationScore()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var counter = entities.System<TerritoryCounterSystem>();
            var initialScore = counter.GetScore("Chimera");
            var map = maps.CreateMap(out var mapId);
            try
            {
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                maps.SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(1));
                entities.AddComponent<GridTerritoryComponent>(grid.Owner);
                var core = entities.SpawnEntity("ExodusChimeraCore", new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
                transform.AnchorEntity((core, entities.GetComponent<TransformComponent>(core)));
                Assert.That(counter.GetScore("Chimera"), Is.GreaterThan(initialScore));
                entities.DeleteEntity(grid.Owner);
                Assert.That(counter.GetScore("Chimera"), Is.EqualTo(initialScore));
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InfestationIffIsOrangeAndItsStatusIsTransient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var entities = client.EntMan;
            var iff = entities.System<IffAffiliationSystem>();
            var shuttles = entities.System<SharedShuttleSystem>();
            var loc = client.ResolveDependency<ILocalizationManager>();
            var grid = entities.Spawn();
            var territory = entities.AddComponent<GridTerritoryComponent>(grid);
            territory.Radius = 2500;
            territory.ColorPoiByFaction = true;
            territory.ControllingFaction = "Chimera";

            Assert.That(iff.TryGetColor(grid, out var color), Is.True);
            Assert.That(color, Is.EqualTo(Color.FromHex("#FF6A00")));
            Assert.That(iff.TryGetLabel(grid, out var label), Is.True);
            Assert.That(label, Is.EqualTo(loc.GetString("territory-faction-infestation")));
            Assert.That(shuttles.GetIFFLabel(grid), Does.Contain(loc.GetString("exodus-iff-infested")));
            Assert.That(shuttles.GetFtlIFFLabel(grid), Does.Contain(loc.GetString("exodus-iff-infested")));
            Assert.That(shuttles.GetFtlIFFLabel(grid), Does.Not.Contain("\n"));
            Assert.That(entities.GetComponent<MetaDataComponent>(grid).EntityName, Does.Not.Contain(loc.GetString("exodus-iff-infested")));

            territory.ControllingFaction = null;
            Assert.That(shuttles.GetFtlIFFLabel(grid), Does.Not.Contain(loc.GetString("exodus-iff-infested")));
            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }
}
