using Content.Server.Administration.Logs;
using Content.Server.Destructible;
using Content.Server.Materials;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Audio;
using Content.Shared.Database;
using Content.Shared.Damage;
using Content.Shared.Materials;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.UserInterface;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningSystem : SharedBulkAutoMiningSystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MaterialStorageSystem _materials = default!;
    [Dependency] private BulkMiningDepositSystem _deposits = default!;
    [Dependency] private BulkMiningConnectivitySystem _connectivity = default!;
    [Dependency] private BulkMiningSurfaceSystem _surface = default!;
    [Dependency] private ShuttleConsoleSystem _shuttleConsole = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private DestructibleSystem _destructible = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private MetaDataSystem _metadata = default!;

    private EntityQuery<BulkAutoMiningEmitterComponent> _emitterQuery;
    private EntityQuery<MaterialStorageComponent> _storageQuery;
    private EntityQuery<ApcPowerReceiverComponent> _powerQuery;
    private EntityQuery<TransformComponent> _xformQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<ShipShieldComponent> _shieldQuery;
    private EntityQuery<BulkMiningSurfaceComponent> _surfaceQuery;

    private static readonly TimeSpan UiInterval = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();
        _emitterQuery = GetEntityQuery<BulkAutoMiningEmitterComponent>();
        _storageQuery = GetEntityQuery<MaterialStorageComponent>();
        _powerQuery = GetEntityQuery<ApcPowerReceiverComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _shieldQuery = GetEntityQuery<ShipShieldComponent>();
        _surfaceQuery = GetEntityQuery<BulkMiningSurfaceComponent>();

        Subs.BuiEvents<BulkAutoMiningConsoleComponent>(BulkAutoMiningUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<BulkAutoMiningSelectGridMessage>(OnSelectGrid);
            subs.Event<BulkAutoMiningStartMessage>(OnStart);
            subs.Event<BulkAutoMiningStopMessage>(OnStop);
            subs.Event<BulkAutoMiningLinkRequestMessage>(OnLinkRequest);
            subs.Event<BulkAutoMiningLinkAcceptMessage>(OnLinkAccept);
            subs.Event<BulkAutoMiningLinkDeclineMessage>(OnLinkDecline);
            subs.Event<BulkAutoMiningLinkBreakMessage>(OnLinkBreak);
        });
        SubscribeLocalEvent<BulkAutoMiningConsoleComponent, MapInitEvent>(OnConsoleMapInit);
        SubscribeLocalEvent<BulkAutoMiningConsoleComponent, ComponentShutdown>(OnConsoleShutdown);
        SubscribeLocalEvent<BulkAutoMiningConsoleComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<BulkAutoMiningConsoleComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<BulkAutoMiningEmitterComponent, PowerChangedEvent>(OnEmitterPowerChanged);
        SubscribeLocalEvent<BulkAutoMiningEmitterComponent, ComponentShutdown>(OnEmitterShutdown);
        SubscribeLocalEvent<BulkAutoMiningEmitterComponent, AnchorStateChangedEvent>(OnEmitterAnchorChanged);
        SubscribeLocalEvent<BulkAutoMiningEmitterComponent, EntParentChangedMessage>(OnEmitterParentChanged);
        InitializeLinks();
    }

    private TimeSpan GetProcessInterval(BulkAutoMiningConsoleComponent console)
    {
        var seconds = console.ProcessInterval > TimeSpan.Zero
            ? console.ProcessInterval.TotalSeconds
            : _cfg.GetCVar(EXCVars.BulkMiningTickInterval);
        return TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Max(0.1, seconds) : 10);
    }

    private void OnConsoleMapInit(Entity<BulkAutoMiningConsoleComponent> ent, ref MapInitEvent args)
    {
        EnsureComp<BulkAutoMiningJobComponent>(ent);
    }

    private void OnConsoleShutdown(Entity<BulkAutoMiningConsoleComponent> ent, ref ComponentShutdown args)
    {
        StopMining(ent);
    }

    private void OnPowerChanged(Entity<BulkAutoMiningConsoleComponent> ent, ref PowerChangedEvent args)
    {
        if (!args.Powered)
            StopMining(ent, "bulk-auto-mining-stopped-power");
    }

    private void OnAnchorChanged(Entity<BulkAutoMiningConsoleComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
            StopMining(ent, "bulk-auto-mining-stopped-unanchored");
    }

    private void OnEmitterPowerChanged(Entity<BulkAutoMiningEmitterComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered)
            return;

        ClearBeam(ent);
        BreakLink(ent, BulkMiningLinkBreakReason.Power);
    }

    private void OnEmitterShutdown(Entity<BulkAutoMiningEmitterComponent> ent, ref ComponentShutdown args)
    {
        BreakLink(ent, BulkMiningLinkBreakReason.Lost);
        StopEmitterAudio(ent);
    }

    private void OnEmitterAnchorChanged(Entity<BulkAutoMiningEmitterComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (!args.Anchored)
        {
            ClearBeam(ent);
            BreakLink(ent, BulkMiningLinkBreakReason.Lost);
            if (ent.Comp.Controller is { } controller && TryComp<BulkAutoMiningConsoleComponent>(controller, out var console))
                StopMining((controller, console), "bulk-auto-mining-stopped-emitter-moved");
        }
    }

    private void OnEmitterParentChanged(Entity<BulkAutoMiningEmitterComponent> ent, ref EntParentChangedMessage args)
    {
        // A linked laser only exists on the ship that made the link, e.g. a grid split carries it away.
        BreakLink(ent, BulkMiningLinkBreakReason.Lost);
        if (ent.Comp.Controller is not { } controller || !TryComp<BulkAutoMiningConsoleComponent>(controller, out var console))
            return;

        if (Transform(ent).GridUid != Transform(controller).GridUid)
            StopMining((controller, console), "bulk-auto-mining-stopped-emitter-moved");
    }

    private void OnUiOpened(Entity<BulkAutoMiningConsoleComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateUi(ent);
    }

    private void OnSelectGrid(Entity<BulkAutoMiningConsoleComponent> ent, ref BulkAutoMiningSelectGridMessage args)
    {
        if (TryGetEntity(args.Grid, out var grid) && grid is { } uid)
            TrySelectGrid(ent, uid);
    }

    private void OnStart(Entity<BulkAutoMiningConsoleComponent> ent, ref BulkAutoMiningStartMessage args)
    {
        if (TryStartMining(ent))
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(args.Actor):player} started bulk mining with {ToPrettyString(ent)}.");
    }

    private void OnStop(Entity<BulkAutoMiningConsoleComponent> ent, ref BulkAutoMiningStopMessage args)
    {
        StopMining(ent, "bulk-auto-mining-stopped-manual");
    }

    public bool TryStartMining(Entity<BulkAutoMiningConsoleComponent> ent)
    {
        if (ent.Comp.Active || !TryComp<BulkAutoMiningJobComponent>(ent, out var job))
            return false;

        if (!IsPoweredAndAnchored(ent))
        {
            Popup(ent, "bulk-auto-mining-stopped-power");
            return false;
        }

        ResolveEmitters(ent, job);
        if (job.Emitters.Count == 0)
        {
            Popup(ent, "bulk-auto-mining-start-no-emitter");
            return false;
        }

        var ready = false;
        foreach (var emitter in job.Emitters)
        {
            if (GetEmitterStatus(ent, emitter) == BulkAutoMiningLaserStatus.Ready)
                ready = true;
        }

        if (!ready)
        {
            Popup(ent, "bulk-auto-mining-start-no-ready-emitter");
            return false;
        }

        job.GridJobs.Clear();
        job.BlockedTiles.Clear();
        job.NextRangeCheckTime = _timing.CurTime;
        ent.Comp.TotalTiles = 0;
        ent.Comp.ProcessedTiles = 0;
        foreach (var target in ent.Comp.SelectedGrids)
        {
            if (!IsTargetInRange(ent, target) || !TryPrepareGrid(ent, target, out var gridJob))
                continue;

            job.GridJobs.Add(gridJob);
            ent.Comp.TotalTiles += gridJob.Remaining;
        }

        if (job.GridJobs.Count == 0)
        {
            Popup(ent, "bulk-auto-mining-start-no-reachable-target");
            UpdateUi(ent);
            return false;
        }

        foreach (var emitter in job.Emitters)
        {
            if (GetEmitterStatus(ent, emitter) == BulkAutoMiningLaserStatus.Ready)
            {
                var comp = _emitterQuery.GetComponent(emitter);
                comp.Controller = ent;
                comp.NextTargetSearchTime = _timing.CurTime;
            }
        }

        foreach (var gridJob in job.GridJobs)
            _connectivity.RetainGrid(ent, gridJob.GridUid);

        ent.Comp.Active = true;
        ProcessMiningTick(ent, job);
        UpdateUi(ent);
        return ent.Comp.Active || ent.Comp.ProcessedTiles > 0;
    }

    public void StopMining(Entity<BulkAutoMiningConsoleComponent> ent, string? popupLocale = null)
    {
        var wasActive = ent.Comp.Active;
        ent.Comp.Active = false;
        if (TryComp<BulkAutoMiningJobComponent>(ent, out var job))
        {
            foreach (var emitter in job.Emitters)
            {
                if (!_emitterQuery.TryComp(emitter, out var comp) || comp.Controller != ent.Owner)
                    continue;

                comp.Controller = null;
                ClearBeam((emitter, comp));
            }

            foreach (var gridJob in job.GridJobs)
            {
                if (!gridJob.Invalidated)
                    ReleaseGridJob(ent, gridJob);
            }

            job.GridJobs.Clear();
            job.Statuses.Clear();
            job.BlockedTiles.Clear();
        }

        if (TerminatingOrDeleted(ent))
            return;

        if (wasActive && popupLocale != null)
            Popup(ent, popupLocale);

        UpdateUi(ent);
    }

    private void ClearBeam(Entity<BulkAutoMiningEmitterComponent> ent)
    {
        if (ent.Comp.BeamGrid == null)
            return;

        SnapshotWarmup(ent);
        ent.Comp.BeamGrid = null;
        StopEmitterAudio(ent);
        Dirty(ent);
    }

    private void StopEmitterAudio(Entity<BulkAutoMiningEmitterComponent> ent)
    {
        ent.Comp.StartupStream = _audio.Stop(ent.Comp.StartupStream);
        _ambient.SetAmbience(ent, false);
    }

    private bool IsPoweredAndAnchored(EntityUid uid)
    {
        return !TerminatingOrDeleted(uid) && !EntityManager.IsQueuedForDeletion(uid) &&
               _xformQuery.TryComp(uid, out var xform) && xform.Anchored &&
               _powerQuery.TryComp(uid, out var power) && power.Powered;
    }

    private BulkAutoMiningLaserStatus GetEmitterStatus(Entity<BulkAutoMiningConsoleComponent> console, EntityUid emitter)
    {
        if (!_emitterQuery.TryComp(emitter, out var comp) || !IsPoweredAndAnchored(emitter) ||
            !_storageQuery.HasComp(emitter) || Transform(emitter).GridUid != Transform(console).GridUid)
            return BulkAutoMiningLaserStatus.Offline;

        if (comp.LinkPartner != null)
            return BulkAutoMiningLaserStatus.Linked;

        if (comp.Controller is { } controller && controller != console.Owner && !TerminatingOrDeleted(controller))
            return BulkAutoMiningLaserStatus.Busy;

        // Reserve enough room for the largest possible yield, including the current warmup bonus.
        var maxYield = GetSlurryYield((emitter, comp), comp.SlurryPerTile.Max);
        if (!_materials.CanChangeMaterialAmount(emitter, comp.SlurryMaterial, maxYield, localOnly: true))
            return BulkAutoMiningLaserStatus.Full;

        return BulkAutoMiningLaserStatus.Ready;
    }

    private void ResolveEmitters(Entity<BulkAutoMiningConsoleComponent> ent, BulkAutoMiningJobComponent job)
    {
        // Active jobs own a fixed set of lasers until stopped; opening another UI cannot change it.
        if (ent.Comp.Active)
            return;

        job.Emitters.Clear();
        if (Transform(ent).GridUid is not { } grid)
            return;

        GetGridEmitters(grid, job.Emitters);
    }

    /// <summary>
    /// Collects the anchored lasers of a ship in a stable order. Lasers are few across the whole server,
    /// so a component query is far cheaper than a lookup over every static entity of the ship.
    /// </summary>
    private void GetGridEmitters(EntityUid grid, List<EntityUid> emitters)
    {
        var query = EntityQueryEnumerator<BulkAutoMiningEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && xform.Anchored && !TerminatingOrDeleted(uid))
                emitters.Add(uid);
        }

        emitters.Sort();
    }

    private void Popup(Entity<BulkAutoMiningConsoleComponent> ent, string locId)
    {
        _popup.PopupEntity(Loc.GetString(locId), ent, PopupType.SmallCaution);
    }
}
