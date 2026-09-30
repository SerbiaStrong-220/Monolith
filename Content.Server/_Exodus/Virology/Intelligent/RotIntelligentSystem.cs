using Content.Server._Exodus.Virology.Lifecycle;
using Content.Shared._Exodus.CCVar;
using Robust.Shared.Configuration;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Maps;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Silicons.StationAi;
using Content.Shared.StationAi;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Intelligent;

/// <summary>Coordinates a colony; spatial ownership and pending work are stored on its core, not in a global cache.</summary>
public sealed partial class RotIntelligentSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private SharedEyeSystem _eyes = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedStationAiSystem _ai = default!;
    [Dependency] private StationAiVisionSystem _vision = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private ThrusterSystem _thrusters = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    [Dependency] private IConfigurationManager _configuration = default!;

    private readonly List<Entity<RotIntelligentComponent, RotColonyStateComponent>> _networkWork = [];
    private int _nextNetworkCore;
    private int _networkBudget = 1;
    [ViewVariables] public int LastNetworkWork { get; private set; }

    private static readonly Vector2i[] Neighbors = [Vector2i.Up, Vector2i.Right, Vector2i.Down, Vector2i.Left];
    private EntityQuery<RotColonyMemberComponent> _members;
    private EntityQuery<TransformComponent> _transforms;
    private EntityQuery<RotIntelligentComponent> _coreQuery;
    private EntityQuery<RotColonyStateComponent> _colonyQuery;
    private EntityQuery<StationAiVisionComponent> _visionQuery;
    private EntityQuery<RotNurseryComponent> _nurseryQuery;
    private EntityQuery<ThrusterComponent> _thrusterQuery;
    private EntityQuery<ShuttleComponent> _shuttleQuery;
    private EntityQuery<MapGridComponent> _mapGridQuery;
    private EntityQuery<MobStateComponent> _mobQuery;

    public override void Initialize()
    {
        base.Initialize();
        _members = GetEntityQuery<RotColonyMemberComponent>();
        _transforms = GetEntityQuery<TransformComponent>();
        _coreQuery = GetEntityQuery<RotIntelligentComponent>();
        _colonyQuery = GetEntityQuery<RotColonyStateComponent>();
        _visionQuery = GetEntityQuery<StationAiVisionComponent>();
        _nurseryQuery = GetEntityQuery<RotNurseryComponent>();
        _thrusterQuery = GetEntityQuery<ThrusterComponent>();
        _shuttleQuery = GetEntityQuery<ShuttleComponent>();
        _mapGridQuery = GetEntityQuery<MapGridComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        SubscribeLocalEvent<RotIntelligentComponent, MapInitEvent>(OnCoreInit);
        SubscribeLocalEvent<RotIntelligentComponent, PlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<RotIntelligentComponent, PlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<RotIntelligentComponent, MobStateChangedEvent>(OnCoreState);
        SubscribeLocalEvent<RotIntelligentComponent, ComponentShutdown>(OnCoreShutdown);
        SubscribeLocalEvent<RotIntelligentComponent, EntParentChangedMessage>(OnCoreParent);
        SubscribeLocalEvent<RotColonyMemberComponent, MapInitEvent>(OnMemberInit);
        SubscribeLocalEvent<RotColonyMemberComponent, ComponentShutdown>(OnMemberShutdown);
        SubscribeLocalEvent<RotColonyMemberComponent, EntParentChangedMessage>(OnMemberParent);
        SubscribeLocalEvent<RotColonyMemberComponent, MobStateChangedEvent>(OnMemberState);
        SubscribeLocalEvent<RotColonyMemberComponent, DamageChangedEvent>(OnMemberDamaged);
        SubscribeLocalEvent<RotColonyMemberComponent, AnchorStateChangedEvent>(OnMemberAnchored);
        SubscribeLocalEvent<RotColonyMemberComponent, MoveEvent>(OnMemberMoved);
        SubscribeLocalEvent<RotColonyMemberComponent, VirusOffspringSpawnedEvent>(OnOffspringSpawned);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        InitializeActions();
        InitializeConstruction();
        InitializeShip();
        InitializeBrood();
        InitializeRooting();
        InitializeChat();
        Subs.CVar(_configuration, EXCVars.RotNetworkBudget, value => _networkBudget = Math.Max(1, value), true);
        SubscribeNetworkEvent<RotVisionRequestEvent>(OnVisionRequest);
        SubscribeNetworkEvent<RotRotateBuildingMessage>(OnRotateBuilding);
    }

    private void OnRotateBuilding(RotRotateBuildingMessage message, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } core
            || !TryComp<RotIntelligentComponent>(core, out var colony)
            || !IsActiveCore(core) || message.Direction == 0)
            return;

        colony.Rotation = (colony.Rotation + Math.Sign(message.Direction) + 4) & 3;
        Dirty(core, colony);
    }

    private void OnCoreInit(Entity<RotIntelligentComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.ChangingForm = false;
        var state = EnsureComp<RotColonyStateComponent>(ent);
        state.NextIncome = _timing.CurTime + ent.Comp.IncomeInterval;
        Join(ent, ent);
        MarkNetworkDirty(ent);
        if (Transform(ent).GridUid is { } grid && TryComp<MapGridComponent>(grid, out var mapGrid))
        {
            state.Grid = grid;
            var origin = _maps.TileIndicesFor(grid, mapGrid, Transform(ent).Coordinates);
            // Map-loaded colonies already contain their territory. Initial tissue is only for a fresh core.
            if (!ent.Comp.Established && ent.Comp.Rooted && state.Members.Count <= 1)
            {
                for (var x = -ent.Comp.InitialRadius; x <= ent.Comp.InitialRadius; x++)
                {
                    for (var y = -ent.Comp.InitialRadius; y <= ent.Comp.InitialRadius; y++)
                    {
                        var tile = origin + new Vector2i(x, y);
                        if (_turf.IsSpace(_maps.GetTileRef(grid, mapGrid, tile)))
                            continue;
                        var occupied = false;
                        foreach (var existing in _maps.GetAnchoredEntities(grid, mapGrid, tile))
                        {
                            if (existing != ent.Owner && _members.HasComp(existing))
                            {
                                occupied = true;
                                break;
                            }
                        }
                        if (occupied)
                            continue;
                        var tissue = Spawn(ent.Comp.Tissue, _maps.GridTileToLocal(grid, mapGrid, tile));
                        Join(tissue, ent);
                    }
                }
            }
        }
        _actions.AddAction(ent, ref ent.Comp.BuildAction, ent.Comp.BuildActionPrototype);
        _actions.AddAction(ent, ref ent.Comp.RootAction, ent.Comp.RootActionPrototype);
        ent.Comp.Established = true;
        if (!ent.Comp.Rooted)
            _transform.Unanchor(ent);
        Dirty(ent);
    }

    public bool IsLivingCore(EntityUid? uid) => uid is { } core && !TerminatingOrDeleted(core)
        && _coreQuery.TryComp(core, out var comp) && comp.Alive && _mobs.IsAlive(core);

    public bool Join(EntityUid member, EntityUid core)
    {
        if (!IsLivingCore(core) || TerminatingOrDeleted(member) || !HasComp<RotCreatureComponent>(member)
            || !_members.TryComp(member, out var comp))
            return false;
        if (comp.Core is { } previous && previous != core && IsLivingCore(previous))
            return false;
        comp.Core = core;
        var state = EnsureComp<RotColonyStateComponent>(core);
        if (comp.NeedsSupport && comp.Connected)
            state.SupportDisconnected = false;

        if (state.Members.Add(member))
        {
            if (comp.Size != Vector2i.Zero)
                MarkNetworkDirty(core, connectivityLost: false);
            else
                RestartMemberPass((core, state));
        }
        if (comp.Size == Vector2i.Zero || !comp.NeedsSupport)
            SetConnected((member, comp), true);
        RefreshVision((member, comp));
        Dirty(member, comp);
        return true;
    }

    public void Inherit(EntityUid parent, EntityUid child)
    {
        if (_members.TryComp(parent, out var member) && member.Core is { } core)
            Join(child, core);
    }

    private void OnOffspringSpawned(Entity<RotColonyMemberComponent> ent, ref VirusOffspringSpawnedEvent args)
    {
        if (ent.Comp.Core is { } core)
            Join(args.Child, core);
    }

    private void OnAttached(Entity<RotIntelligentComponent> ent, ref PlayerAttachedEvent args) => RestoreEye(ent);

    private void RestoreEye(Entity<RotIntelligentComponent> ent)
    {
        if (!IsActiveCore(ent))
            return;
        if (ent.Comp.Eye == null || TerminatingOrDeleted(ent.Comp.Eye.Value))
        {
            var eye = Spawn(ent.Comp.EyePrototype, Transform(ent).Coordinates);
            var camera = EnsureComp<RotIntelligentEyeComponent>(eye);
            camera.Core = ent;
            Dirty(eye, camera);
            ent.Comp.Eye = eye;
            Dirty(ent);
        }
        AttachEye(ent);
    }

    private void AttachEye(Entity<RotIntelligentComponent> ent)
    {
        if (!IsActiveCore(ent) || ent.Comp.Eye is not { } eye || TerminatingOrDeleted(eye))
            return;
        _eyes.SetDrawFov(ent, false);
        _eyes.SetTarget(ent, eye);
        _mover.SetRelay(ent, eye);
        if (TryComp<RotColonyStateComponent>(ent, out var state))
        {
            state.NextVision = TimeSpan.Zero;
            state.ViewFrame = null;
        }
    }

    private void OnDetached(Entity<RotIntelligentComponent> ent, ref PlayerDetachedEvent args)
    {
        if (TryComp<RotColonyStateComponent>(ent, out var state) && state.Rooting is { } rooting)
            _doAfter.Cancel(rooting);
        ClearEye(ent);
    }

    private void ClearEye(Entity<RotIntelligentComponent> ent)
    {
        StopPiloting(ent);
        _ui.CloseUi(ent.Owner, RotIntelligentUiKey.Key);
        if (ent.Comp.Eye is { } eye)
            QueueDel(eye);
        ent.Comp.Eye = null;

        if (TryComp<EyeComponent>(ent, out var eyeComponent))
        {
            _eyes.SetTarget(ent, null, eyeComponent);
            _eyes.SetDrawFov(ent, true, eyeComponent);
        }

        RemCompDeferred<Content.Shared.Movement.Components.RelayInputMoverComponent>(ent);
        Dirty(ent);
    }

    private void OnCoreState(Entity<RotIntelligentComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive)
            ShutDownColony(ent);
    }

    private void OnCoreShutdown(Entity<RotIntelligentComponent> ent, ref ComponentShutdown args) => ShutDownColony(ent);

    private void ShutDownColony(Entity<RotIntelligentComponent> ent)
    {
        if (!ent.Comp.Alive)
            return;
        ent.Comp.Alive = false;
        ent.Comp.NetworkReady = false;
        ent.Comp.ConstructionAvailable = false;
        ent.Comp.Income = 0;
        RefreshRootActions(ent);
        ClearEye(ent);
        if (!TryComp<RotColonyStateComponent>(ent, out var state))
            return;
        state.Scratch.Clear();
        state.Scratch.AddRange(state.Projects);
        foreach (var job in state.Scratch)
            CancelProject(job, refund: false);
        foreach (var uid in state.Members)
        {
            if (!_members.TryComp(uid, out var member))
                continue;
            SetConnected((uid, member), false);
            member.Core = null;
            Dirty(uid, member);
        }
        state.Members.Clear();
        state.Cells.Clear();
        state.BuildingCells.Clear();
        state.NetworkEnumerator.Dispose();
        state.NetworkPhase = RotNetworkPhase.Idle;
        state.WatchedCells.Clear();
        state.BuildingWatchedCells.Clear();
        state.Reservations.Clear();
        state.Connected.Clear();
        state.BuildingConnected.Clear();
        state.Frontier.Clear();
        state.Alerts.Clear();
        state.Rally = null;
        if (state.RallyMarker is { } marker)
            QueueDel(marker);
        state.RallyMarker = null;
        Dirty(ent);
    }

    private void OnCoreParent(Entity<RotIntelligentComponent> ent, ref EntParentChangedMessage args)
    {
        StopPiloting(ent);
        MarkNetworkDirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        _networkWork.Clear();
        var query = EntityQueryEnumerator<RotIntelligentComponent, RotColonyStateComponent>();
        while (query.MoveNext(out var uid, out var core, out var state))
        {
            if (!core.Alive || TerminatingOrDeleted(uid))
                continue;
            if (!state.ProjectsRestored)
            {
                RestoreProjects((uid, core, state));
                RefreshRootActions((uid, core));
            }
            if (!core.Rooted)
            {
                core.Income = 0;
                state.NextIncome = now + core.IncomeInterval;
                continue;
            }
            _networkWork.Add((uid, core, state));
            if (now >= state.NextIncome)
            {
                state.NextIncome += core.IncomeInterval;
                var biomass = MathF.Min(core.Capacity, core.Biomass + core.Income * (float)core.IncomeInterval.TotalSeconds);
                if (biomass != core.Biomass)
                {
                    core.Biomass = biomass;
                    Dirty(uid, core);
                }
                ValidateProjects((uid, core, state));
                RefreshMembers((uid, core, state));
            }
            if (now >= state.NextVision && HasComp<ActorComponent>(uid))
            {
                state.NextVision = now + core.VisionInterval;
                UpdateVision((uid, core, state));
            }
            if (now >= state.NextUi)
            {
                state.NextUi = now + core.UiInterval;
                UpdateUi((uid, core, state));
            }
        }
        var budget = _networkBudget;
        LastNetworkWork = 0;
        var count = _networkWork.Count;
        for (var i = 0; i < count && budget > 0; i++)
        {
            var index = (_nextNetworkCore + i) % count;
            var colony = _networkWork[index];
            var used = UpdateNetwork(colony, Math.Min(budget, Math.Max(1, colony.Comp1.NetworkBudget)));
            budget -= used;
            LastNetworkWork += used;
            if (budget == 0 || i == count - 1)
                _nextNetworkCore = (index + 1) % count;
        }
        UpdateBrood();
    }
}
