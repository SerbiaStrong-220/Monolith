using System.Numerics;
using Content.Server._Exodus.Shuttles.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Shuttles.Systems;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Shuttles.Systems;

/// <summary>
/// Tracks working FTL suppressors and blocks FTL jumps that start inside their fields or end inside them.
/// Console-initiated jumps that are still spooling up get aborted when a field covers the shuttle or its target.
/// </summary>
public sealed partial class FtlSuppressorSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PowerReceiverSystem _power = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private ShuttleSystem _shuttle = default!;

    public const string LeaveRejection = "ftl-suppressor-leave-blocked";
    public const string TargetRejection = "ftl-suppressor-target-blocked";
    public const string SpoolAborted = "ftl-suppressor-spool-aborted";

    /// <summary>
    /// Minimal delay between two shuttle console refreshes caused by suppressors, so flickering power can't spam them.
    /// </summary>
    private static readonly TimeSpan ConsoleRefreshCooldown = TimeSpan.FromSeconds(1);

    private EntityQuery<FTLComponent> _ftlQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private readonly List<(EntityUid Shuttle, EntityUid Suppressor)> _abortedSpools = [];
    private readonly List<EntityUid> _finishedSpools = [];
    private bool _refreshAllConsoles;
    private bool _refreshOpenConsoles;
    private TimeSpan _nextConsoleRefresh;

    public override void Initialize()
    {
        base.Initialize();

        _ftlQuery = GetEntityQuery<FTLComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<FtlSuppressorComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<FtlSuppressorComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<FtlSuppressorComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<FtlSuppressorComponent, ComponentShutdown>(OnSuppressorShutdown);
        SubscribeLocalEvent<FtlSuppressorComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ActiveFtlSuppressorComponent, ComponentShutdown>(OnActiveShutdown);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnConsoleFTLAttempt);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        UpdateZonePositions(curTime);
        UpdateSpoolingShuttles();
        RefreshConsoles(curTime);
    }

    /// <summary>
    /// True if any suppressor is currently projecting its field.
    /// </summary>
    public bool HasActiveSuppressors()
    {
        return Count<ActiveFtlSuppressorComponent>() > 0;
    }

    /// <summary>
    /// True if the map position lies inside the field of any working suppressor.
    /// </summary>
    public bool IsSuppressed(MapCoordinates coordinates)
    {
        return TryGetSuppressor(coordinates, out _);
    }

    /// <summary>
    /// Finds a working suppressor whose field covers the map position.
    /// </summary>
    public bool TryGetSuppressor(MapCoordinates coordinates, out EntityUid suppressor)
    {
        suppressor = EntityUid.Invalid;

        if (coordinates.MapId == MapId.Nullspace)
            return false;

        var query = AllEntityQuery<ActiveFtlSuppressorComponent, FtlSuppressorComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var comp, out var xform))
        {
            if (xform.MapID != coordinates.MapId)
                continue;

            if (Vector2.DistanceSquared(_transform.GetWorldPosition(xform), coordinates.Position) > comp.Range * comp.Range)
                continue;

            suppressor = uid;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds a working suppressor whose field covers the shuttle's center of mass.
    /// </summary>
    public bool TryGetShuttleSuppressor(EntityUid shuttle, out EntityUid suppressor)
    {
        suppressor = EntityUid.Invalid;

        if (!_xformQuery.TryComp(shuttle, out var xform) || xform.MapID == MapId.Nullspace)
            return false;

        var position = _physicsQuery.TryComp(shuttle, out var physics)
            ? _map.GetGridPosition((shuttle, physics, xform))
            : _transform.GetWorldPosition(xform);

        return TryGetSuppressor(new MapCoordinates(position, xform.MapID), out suppressor);
    }

    /// <summary>
    /// True if a shuttle may arrive at the target.
    /// On false, <paramref name="rejection"/> is a localization id describing why.
    /// </summary>
    public bool CanFTLTo(EntityCoordinates target, out string rejection)
    {
        rejection = string.Empty;

        if (!IsSuppressed(_transform.ToMapCoordinates(target, false)))
            return true;

        rejection = TargetRejection;
        return false;
    }

    /// <summary>
    /// Adds the fields of all working suppressors as FTL exclusion zones for the shuttle console map.
    /// Coordinates are map-relative so clients can resolve them without the suppressor's grid in PVS.
    /// </summary>
    public void GetExclusions(ref List<ShuttleExclusionObject>? exclusions)
    {
        var query = AllEntityQuery<ActiveFtlSuppressorComponent, FtlSuppressorComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var comp, out var xform))
        {
            if (xform.MapUid is not { } mapUid)
                continue;

            var coordinates = new NetCoordinates(GetNetEntity(mapUid), _transform.GetWorldPosition(xform));
            exclusions ??= [];
            exclusions.Add(new ShuttleExclusionObject(coordinates, comp.Range, Loc.GetString(comp.ZoneName), comp.ZoneFill));
        }
    }

    /// <summary>
    /// Lets a freshly started console FTL jump be interrupted by suppressors while it spools up.
    /// </summary>
    public void TrackConsoleSpool(EntityUid shuttle)
    {
        if (!_ftlQuery.TryComp(shuttle, out var ftl) || ftl.State != FTLState.Starting)
            return;

        EnsureComp<FtlSuppressionSpoolComponent>(shuttle);
    }

    private void OnMapInit(Entity<FtlSuppressorComponent> ent, ref MapInitEvent args)
    {
        UpdateActiveState(ent);
    }

    private void OnPowerChanged(Entity<FtlSuppressorComponent> ent, ref PowerChangedEvent args)
    {
        UpdateActiveState(ent);
    }

    private void OnAnchorChanged(Entity<FtlSuppressorComponent> ent, ref AnchorStateChangedEvent args)
    {
        UpdateActiveState(ent);
    }

    private void OnSuppressorShutdown(Entity<FtlSuppressorComponent> ent, ref ComponentShutdown args)
    {
        if (!TerminatingOrDeleted(ent))
            RemCompDeferred<ActiveFtlSuppressorComponent>(ent);
    }

    private void OnActiveShutdown(Entity<ActiveFtlSuppressorComponent> ent, ref ComponentShutdown args)
    {
        _refreshAllConsoles = true;
    }

    private void OnExamined(Entity<FtlSuppressorComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(FtlSuppressorComponent)))
        {
            args.PushMarkup(Loc.GetString("ftl-suppressor-examine-range", ("range", (int) ent.Comp.Range)));
            args.PushMarkup(Loc.GetString(HasComp<ActiveFtlSuppressorComponent>(ent)
                ? "ftl-suppressor-examine-active"
                : "ftl-suppressor-examine-inactive"));
        }
    }

    private void OnConsoleFTLAttempt(ref ConsoleFTLAttemptEvent args)
    {
        if (args.Cancelled || !TryGetShuttleSuppressor(args.Uid, out _))
            return;

        args.Cancelled = true;
        args.Reason = Loc.GetString(LeaveRejection);
    }

    private void UpdateActiveState(Entity<FtlSuppressorComponent> ent)
    {
        if (TerminatingOrDeleted(ent))
            return;

        var xform = Transform(ent);
        var active = xform.Anchored && _power.IsPowered(ent.Owner);
        if (active == HasComp<ActiveFtlSuppressorComponent>(ent))
            return;

        if (!active)
        {
            RemComp<ActiveFtlSuppressorComponent>(ent);
            return;
        }

        var activeComp = AddComp<ActiveFtlSuppressorComponent>(ent);
        activeComp.NextZoneRefresh = _timing.CurTime + ent.Comp.ZoneRefreshInterval;
        activeComp.PublishedPosition = _transform.GetMapCoordinates(xform);
        _refreshAllConsoles = true;
    }

    /// <summary>
    /// Suppressors on moving grids drag their fields along; resend the zones to open consoles once they moved noticeably.
    /// </summary>
    private void UpdateZonePositions(TimeSpan curTime)
    {
        var query = EntityQueryEnumerator<ActiveFtlSuppressorComponent, FtlSuppressorComponent, TransformComponent>();
        while (query.MoveNext(out _, out var active, out var comp, out var xform))
        {
            if (curTime < active.NextZoneRefresh)
                continue;

            active.NextZoneRefresh += comp.ZoneRefreshInterval;

            var position = _transform.GetMapCoordinates(xform);
            if (position.MapId == active.PublishedPosition.MapId &&
                Vector2.DistanceSquared(position.Position, active.PublishedPosition.Position) < comp.ZoneRefreshDistance * comp.ZoneRefreshDistance)
            {
                continue;
            }

            active.PublishedPosition = position;
            _refreshOpenConsoles = true;
        }
    }

    private void UpdateSpoolingShuttles()
    {
        var query = EntityQueryEnumerator<FtlSuppressionSpoolComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (!_ftlQuery.TryComp(uid, out var ftl) || ftl.State != FTLState.Starting || ftl.LinkedShuttle != null)
            {
                _finishedSpools.Add(uid);
                continue;
            }

            if (TryGetShuttleSuppressor(uid, out var suppressor) ||
                TryGetSuppressor(_transform.ToMapCoordinates(ftl.TargetCoordinates, false), out suppressor))
            {
                _abortedSpools.Add((uid, suppressor));
            }
        }

        foreach (var uid in _finishedSpools)
        {
            RemComp<FtlSuppressionSpoolComponent>(uid);
        }

        _finishedSpools.Clear();

        foreach (var (shuttle, suppressor) in _abortedSpools)
        {
            RemComp<FtlSuppressionSpoolComponent>(shuttle);

            if (_shuttle.TryAbortFTLStartup(shuttle))
                NotifySpoolAborted(shuttle, suppressor);
        }

        _abortedSpools.Clear();
    }

    private void NotifySpoolAborted(EntityUid shuttle, EntityUid suppressor)
    {
        var sound = CompOrNull<FtlSuppressorComponent>(suppressor)?.AbortSound;
        var query = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var console, out _, out var xform))
        {
            if (xform.GridUid != shuttle)
                continue;

            _popup.PopupEntity(Loc.GetString(SpoolAborted), console, PopupType.MediumCaution);
            _audio.PlayPvs(sound, console);
        }
    }

    private void RefreshConsoles(TimeSpan curTime)
    {
        if (!_refreshAllConsoles && !_refreshOpenConsoles || curTime < _nextConsoleRefresh)
            return;

        if (_refreshAllConsoles)
            _console.RefreshShuttleConsoles();
        else
            _console.RefreshOpenShuttleConsoles();

        _refreshAllConsoles = false;
        _refreshOpenConsoles = false;
        _nextConsoleRefresh = curTime + ConsoleRefreshCooldown;
    }
}
