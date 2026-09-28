using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Content.Server._Exodus.Body;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.NPC.HTN;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Atmos;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Movement.Components;
using Robust.Client.Input;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class RotIntelligentTest
{
    [Test]
    public async Task MobileCoreKeepsItsBodyAndReturnsToRootedControls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true }, new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid organ = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -2; x <= 14; x++)
                for (var y = -2; y <= 2; y++)
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            organ = em.SpawnEntity("RotEyeball", new EntityCoordinates(map.Grid, 1.5f, 1.5f));
            em.System<RotIntelligentSystem>().Join(organ, core);
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            brain.RootDuration = TimeSpan.FromSeconds(.2);
            brain.BaseIncome = 0;
            server.PlayerMan.SetAttachedEntity(pair.Player!, core);
        });
        await pair.RunSeconds(.6f);
        float biomass = 0;
        await server.WaitAssertion(() =>
        {
            var system = em.System<RotIntelligentSystem>();
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            biomass = brain.Biomass;
            Assert.That(system.TryToggleRoot((core, brain)), Is.True);
            Assert.That(system.TryToggleRoot((core, brain)), Is.False, "Repeated activation must not create another transition.");
        });
        await pair.RunSeconds(.6f);
        await server.WaitAssertion(() =>
        {
            var system = em.System<RotIntelligentSystem>();
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            Assert.Multiple(() =>
            {
                Assert.That(brain.Rooted, Is.False);
                Assert.That(brain.Eye, Is.Null);
                Assert.That(em.GetComponent<TransformComponent>(core).Anchored, Is.False);
                Assert.That(em.HasComponent<RelayInputMoverComponent>(core), Is.False);
                Assert.That(brain.Biomass, Is.EqualTo(biomass));
                Assert.That(em.GetComponent<RotColonyMemberComponent>(organ).Core, Is.EqualTo(core));
                Assert.That(em.GetComponent<RotColonyMemberComponent>(organ).Connected, Is.False);
                Assert.That(system.TryPilot((core, brain)), Is.False);
                Assert.That(system.TryQueueBuilding((core, brain), new EntityCoordinates(map.Grid, 2.5f, .5f), "RotBuildTissue", 0), Is.False);
            });
        });
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client.EntMan;
            var uid = pair.ToClientUid(core);
            Assert.That(client.HasComponent<Content.Client._Exodus.StationAi.CameraViewMaskComponent>(uid), Is.False);
            Assert.That(client.System<Robust.Client.GameObjects.SpriteSystem>().LayerGetRsiState(uid, 0).ToString(), Is.EqualTo("mobile"));
            Assert.That(client.GetComponent<Robust.Client.GameObjects.SpriteComponent>(uid).Scale, Is.EqualTo(Vector2.One));
        });
        foreach (var keyState in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            await pair.Client.WaitPost(() =>
            {
                var client = pair.Client;
                var timing = client.ResolveDependency<IGameTiming>();
                var input = client.ResolveDependency<IInputManager>();
                var key = EngineKeyFunctions.MoveRight;
                var message = new ClientFullInputCmdMessage(timing.CurTick, timing.TickFraction, input.NetworkBindMap.KeyFunctionID(key))
                {
                    State = keyState,
                };
                client.EntMan.System<Robust.Client.GameObjects.InputSystem>().HandleInputCommand(client.Session!, key, message);
            });
            await pair.RunSeconds(.3f);
        }
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(core).LocalPosition.X, Is.GreaterThan(.7f), "Movement keys must move the body after removing its eye relay.");
            em.System<SharedTransformSystem>().SetCoordinates(core, new EntityCoordinates(map.Grid, 12.5f, .5f));
        });
        await server.WaitAssertion(() => Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, em.GetComponent<RotIntelligentComponent>(core))), Is.True));
        await pair.RunSeconds(.8f);
        await server.WaitAssertion(() =>
        {
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            Assert.That(brain.Rooted, Is.True);
            Assert.That(brain.NetworkReady, Is.True);
            Assert.That(brain.Eye, Is.Not.Null);
            Assert.That(em.GetComponent<TransformComponent>(core).Anchored, Is.True);
            Assert.That(em.GetComponent<RotColonyMemberComponent>(organ).Connected, Is.False);
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Cells.ContainsKey(new Vector2i(13, 0)), Is.False,
                "Taking root must not grant another free starting patch.");
        });
        await pair.Client.WaitAssertion(() => Assert.That(pair.Client.EntMan.HasComponent<Content.Client._Exodus.StationAi.CameraViewMaskComponent>(pair.ToClientUid(core)), Is.True));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AutomaticGrowthConvertsWallsAndDoorsWithoutExtendingItsRadius()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        EntityUid wall = default;
        EntityUid door = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 9; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            wall = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 2.5f, .5f));
            door = em.SpawnEntity("Airlock", new EntityCoordinates(map.Grid, 4.5f, .5f));
            em.System<SharedDoorSystem>().OnPartialOpen(door);
            em.System<SharedDoorSystem>().SetState(door, DoorState.Open);
            em.System<SharedTransformSystem>().SetLocalRotation(door, Angle.FromDegrees(90));
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            AccelerateGrowth(em, core, server.ResolveDependency<IGameTiming>().CurTime);
        });
        await pair.RunSeconds(5);
        EntityUid converted = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(em.EntityExists(wall), Is.False);
            Assert.That(em.EntityExists(door), Is.False);
            var members = em.EntityQueryEnumerator<RotColonyMemberComponent, TransformComponent>();
            var doors = 0;
            while (members.MoveNext(out var uid, out var member, out var xform))
            {
                if (member.Core != core || member.Size == Vector2i.Zero)
                    continue;
                Assert.That(xform.LocalPosition.X, Is.LessThan(6));
                if (uid != core)
                    Assert.That(em.HasComponent<RotSpreadComponent>(uid), Is.False, "Converted structures cannot propagate another radius.");
                if (em.TryGetComponent<DoorComponent>(uid, out var organicDoor))
                {
                    doors++;
                    Assert.That(organicDoor.State, Is.EqualTo(DoorState.Open));
                    Assert.That(em.GetComponent<PhysicsComponent>(uid).CanCollide, Is.False);
                    Assert.That(em.GetComponent<AirtightComponent>(uid).AirBlocked, Is.False);
                    Assert.That(xform.LocalRotation, Is.EqualTo(Angle.FromDegrees(90)));
                    Assert.That(member.Refund, Is.Zero);
                }
                if (Math.Abs(xform.LocalPosition.X - 2.5f) < .01f && !member.Tissue)
                {
                    converted = uid;
                    Assert.That(em.GetComponent<AirtightComponent>(uid).AirBlocked, Is.True);
                }
            }
            Assert.That(doors, Is.EqualTo(1));
            Assert.That(converted, Is.Not.EqualTo(default(EntityUid)));
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Cells.ContainsKey(new Vector2i(5, 0)), Is.True);
            em.DeleteEntity(converted);
        });
        await pair.RunSeconds(3);
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var tissue = false;
            foreach (var uid in maps.GetAnchoredEntities(map.Grid.Owner, map.Grid.Comp, new Vector2i(2, 0)))
            {
                if (em.TryGetComponent<RotColonyMemberComponent>(uid, out var member))
                {
                    Assert.That(member.Tissue, Is.True, "Removing an organic wall leaves a passage, never a replacement wall.");
                    tissue = true;
                }
            }
            Assert.That(tissue, Is.True);
            Assert.That(em.GetComponent<RotSpreadGridComponent>(map.Grid).Reservations, Is.Empty);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task HabitatProtectsRespirationAndVacuumWithoutBlockingCombatDamage()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var mob = em.SpawnEntity("MobRotSpawn", map.GridCoords);
            em.RemoveComponent<HTNComponent>(mob);
            var tissue = em.SpawnEntity("RotTissue", map.GridCoords);
            var habitat = em.System<RotHabitatSystem>();
            Assert.That(habitat.IsSheltered(mob), Is.True);
            var lungs = em.GetComponent<RespiratorComponent>(mob);
            em.System<Content.Server.Body.Systems.RespiratorSystem>().UpdateSaturation(mob, -lungs.Saturation, lungs);
            var respiration = new RespirationAttemptEvent();
            em.EventBus.RaiseLocalEvent(mob, ref respiration);
            Assert.That(respiration.Handled, Is.True);
            Assert.That(lungs.Saturation, Is.EqualTo(lungs.MaxSaturation));
            var barotrauma = em.GetComponent<BarotraumaComponent>(mob);
            Assert.That(em.System<BarotraumaSystem>().GetFeltLowPressure(mob, barotrauma, 1), Is.EqualTo(Atmospherics.OneAtmosphere));
            var cold = new EnvironmentDamageAttemptEvent(EnvironmentHazard.Cold);
            em.EventBus.RaiseLocalEvent(mob, ref cold);
            Assert.That(cold.Cancelled, Is.True);
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Cold", 2);
            damage.DamageDict.Add("Heat", 2);
            damage.DamageDict.Add("Blunt", 2);
            em.System<DamageableSystem>().TryChangeDamage(mob, damage, true);
            Assert.That(em.GetComponent<DamageableComponent>(mob).TotalDamage.Float(), Is.EqualTo(6));
            em.DeleteEntity(tissue);
            Assert.That(habitat.IsSheltered(mob), Is.False);
            Assert.That(em.System<BarotraumaSystem>().GetFeltLowPressure(mob, barotrauma, 1), Is.EqualTo(1));
            cold = new EnvironmentDamageAttemptEvent(EnvironmentHazard.Cold);
            em.EventBus.RaiseLocalEvent(mob, ref cold);
            Assert.That(cold.Cancelled, Is.False);
            var vine = em.SpawnEntity("RotInfectedVine", map.GridCoords);
            Assert.That(habitat.IsSheltered(mob), Is.True);
            em.DeleteEntity(vine);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task GrowthBudgetIsSharedAcrossEightyGrids()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var cores = new List<EntityUid>();
        var oldWork = server.CfgMan.GetCVar(EXCVars.RotSpreadBudget);
        var oldGrowth = server.CfgMan.GetCVar(EXCVars.RotSpreadMutationBudget);
        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(EXCVars.RotSpreadBudget, 24);
            server.CfgMan.SetCVar(EXCVars.RotSpreadMutationBudget, 2);
            var maps = em.System<SharedMapSystem>();
            var manager = server.ResolveDependency<IMapManager>();
            var transform = em.System<SharedTransformSystem>();
            for (var i = 0; i < 80; i++)
            {
                var grid = i == 0 ? map.Grid : manager.CreateGridEntity(map.MapId);
                transform.SetWorldPosition(grid, new Vector2(i * 32, 0));
                for (var x = 0; x <= 3; x++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
                var core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(grid, .5f, .5f));
                AccelerateGrowth(em, core, server.ResolveDependency<IGameTiming>().CurTime);
                cores.Add(core);
            }
        });
        for (var tick = 0; tick < 250; tick++)
        {
            await server.WaitRunTicks(1);
            await server.WaitAssertion(() =>
            {
                var growth = em.System<RotSpreadSystem>();
                Assert.That(growth.LastWork, Is.InRange(0, 24));
                Assert.That(growth.LastGrowth, Is.InRange(0, 2));
            });
        }
        await server.WaitAssertion(() =>
        {
            foreach (var core in cores)
                Assert.That(em.GetComponent<RotColonyStateComponent>(core).Cells.ContainsKey(new Vector2i(3, 0)), Is.True, "Every grid must eventually receive its share of growth.");
            server.CfgMan.SetCVar(EXCVars.RotSpreadBudget, oldWork);
            server.CfgMan.SetCVar(EXCVars.RotSpreadMutationBudget, oldGrowth);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task InterruptedRootingAndSavedMobileCorePreserveState()
    {
        await using var pair = await PoolManager.GetServerClient(testContext: new LiveContext());
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid core = default;
        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = -2; x <= 14; x++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, 0), map.Tile.Tile);
            core = em.SpawnEntity("MobRotIntelligent", new EntityCoordinates(map.Grid, .5f, .5f));
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            brain.RootDuration = TimeSpan.FromSeconds(.5);
            brain.BaseIncome = 0;
            brain.Biomass = 73;
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() => Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, em.GetComponent<RotIntelligentComponent>(core))), Is.True));
        await pair.RunSeconds(.1f);
        await server.WaitAssertion(() =>
        {
            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", 5);
            em.System<DamageableSystem>().TryChangeDamage(core, damage, true);
        });
        await pair.RunSeconds(.6f);
        await server.WaitAssertion(() =>
        {
            var brain = em.GetComponent<RotIntelligentComponent>(core);
            Assert.That(brain.Rooted, Is.True);
            Assert.That(brain.ChangingForm, Is.False);
            Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, brain)), Is.True);
        });
        await pair.RunSeconds(.7f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).Rooted, Is.False);
            Assert.That(em.GetComponent<DamageableComponent>(core).TotalDamage.Float(), Is.EqualTo(5), "Changing form must preserve damage.");
            em.System<SharedTransformSystem>().SetCoordinates(core, new EntityCoordinates(map.Grid, 12.5f, .5f));
            var obstruction = em.SpawnEntity("WallSolid", new EntityCoordinates(map.Grid, 12.5f, .5f));
            Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, em.GetComponent<RotIntelligentComponent>(core))), Is.False);
            em.DeleteEntity(obstruction);
            var loader = em.System<MapLoaderSystem>();
            using var text = new StringWriter();
            Assert.That(loader.TrySaveGrid(map.Grid.Owner, text), Is.True);
            em.DeleteEntity(map.Grid);
            using var reader = new StringReader(text.ToString());
            Assert.That(loader.TryLoadGrid(reader, "rot-mobile-save", out _, out _,
                DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
        });
        await pair.RunSeconds(.5f);
        await server.WaitAssertion(() =>
        {
            var cores = em.EntityQueryEnumerator<RotIntelligentComponent>();
            Assert.That(cores.MoveNext(out core, out var brain), Is.True);
            Assert.That(brain.Rooted, Is.False);
            Assert.That(brain.ChangingForm, Is.False);
            Assert.That(brain.Biomass, Is.EqualTo(73));
            Assert.That(em.GetComponent<TransformComponent>(core).Anchored, Is.False);
            Assert.That(em.System<RotIntelligentSystem>().TryToggleRoot((core, brain)), Is.True);
        });
        await pair.RunSeconds(.7f);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<RotIntelligentComponent>(core).Rooted, Is.True);
            Assert.That(em.GetComponent<RotColonyStateComponent>(core).Cells.ContainsKey(new Vector2i(13, 0)), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    private static void AccelerateGrowth(IEntityManager em, EntityUid uid, TimeSpan now)
    {
        var growth = em.GetComponent<RotSpreadComponent>(uid);
        growth.Interval = TimeSpan.FromSeconds(.1);
        growth.ConversionDuration = TimeSpan.FromSeconds(.1);
        growth.ConversionSecondsPerDamage = 0;
        growth.NextGrowth = now + growth.Interval;
    }
}
