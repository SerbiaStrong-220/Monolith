using Content.Server.Atmos.EntitySystems;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Maps;
using Content.Shared.DoAfter;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Intelligent;

/// <summary>Incremental, cardinal expansion with a shared work budget and per-organ radius.</summary>
public sealed partial class RotSpreadSystem : EntitySystem
{
    [Dependency] private SharedMapSystem _maps = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private RotIntelligentSystem _colony = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    private EntityQuery<RotSpreadComponent> _sources;
    private EntityQuery<RotSpreadGridComponent> _grids;
    private EntityQuery<RotColonyMemberComponent> _members;
    private readonly List<Entity<RotSpreadComponent>> _work = [];
    private readonly List<Vector2i> _deferred = [];
    private int _cursor;
    private int _budget = 1;
    private int _growthBudget = 1;
    private static readonly Vector2i[] Neighbors = [Vector2i.Up, Vector2i.Right, Vector2i.Down, Vector2i.Left];
    [ViewVariables] public int LastWork { get; private set; }
    [ViewVariables] public int LastGrowth { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        _sources = GetEntityQuery<RotSpreadComponent>();
        _grids = GetEntityQuery<RotSpreadGridComponent>();
        _members = GetEntityQuery<RotColonyMemberComponent>();
        Subs.CVar(_configuration, EXCVars.RotSpreadBudget, value => _budget = Math.Max(1, value), true);
        Subs.CVar(_configuration, EXCVars.RotSpreadMutationBudget, value => _growthBudget = Math.Max(1, value), true);
        InitializeConversion();
        SubscribeLocalEvent<RotSpreadComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<RotSpreadComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RotSpreadComponent, MoveEvent>(OnMoved);
        SubscribeLocalEvent<RotSpreadComponent, RotConnectionChangedEvent>(OnConnection);
        SubscribeLocalEvent<TransformComponent, AnchorStateChangedEvent>(OnAnchor);
        SubscribeLocalEvent<AirtightChanged>(OnAirtight);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
    }

    private void OnInit(Entity<RotSpreadComponent> ent, ref MapInitEvent args)
    {
        // Map saves contain the grown entities; runtime frontiers are rebuilt from their actual contents.
        if (ent.Comp.PendingMarker is { } marker && !TerminatingOrDeleted(marker))
            QueueDel(marker);
        ent.Comp.PendingMarker = null;
        ent.Comp.NextGrowth = _timing.CurTime + ent.Comp.Interval;
        Register(ent);
    }

    private void OnShutdown(Entity<RotSpreadComponent> ent, ref ComponentShutdown args) => Unregister(ent);

    private void OnMoved(Entity<RotSpreadComponent> ent, ref MoveEvent args)
    {
        if (!Transform(ent).Anchored)
        {
            if (ent.Comp.Grid != null)
                Unregister(ent);
            return;
        }
        Register(ent);
    }

    private void OnConnection(Entity<RotSpreadComponent> ent, ref RotConnectionChangedEvent args)
    {
        ent.Comp.Active = args.Connected;
        ent.Comp.Rebuild = true;
        if (!args.Connected)
            CancelConversion(ent);
    }

    private void OnAnchor(Entity<TransformComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (_sources.TryComp(ent, out var source))
            Register((ent, source));
        // Adding our tissue cannot invalidate a frontier; deleting it or adding obstacles can.
        if (ent.Comp.Anchored && _members.HasComp(ent))
            return;
        InvalidateCell(ent);
    }

    private void OnAirtight(ref AirtightChanged args) => Wake(args.Position.Grid, args.Position.Tile);

    private void OnTileChanged(ref TileChangedEvent args)
    {
        foreach (var change in args.Changes)
            Wake(args.Entity, change.GridIndices);
    }

    public void InvalidateCell(Entity<TransformComponent> ent)
    {
        var xform = ent.Comp;
        if (xform.GridUid is { } grid && _grids.HasComp(grid) && TryComp<MapGridComponent>(grid, out var mapGrid))
            Wake(grid, _maps.TileIndicesFor(grid, mapGrid, xform.Coordinates));
    }

    private void Wake(EntityUid grid, Vector2i tile)
    {
        if (!_grids.TryComp(grid, out var index) || !index.Watchers.TryGetValue(tile, out var watchers))
            return;
        foreach (var uid in watchers)
        {
            if (_sources.TryComp(uid, out var source))
                source.Rebuild = true;
        }
    }

    private void Register(Entity<RotSpreadComponent> ent)
    {
        var xform = Transform(ent);
        if (xform.Anchored && ent.Comp.Grid == xform.GridUid && ent.Comp.Grid is { } previous
            && TryComp<MapGridComponent>(previous, out var previousGrid)
            && ent.Comp.Origin == _maps.TileIndicesFor(previous, previousGrid, xform.Coordinates)
            && (ent.Comp.Size == Vector2i.One || ent.Comp.Rotation == RotGeometry.QuarterTurns(xform.LocalRotation)))
            return;
        Unregister(ent);
        if (!xform.Anchored || xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid)
            || !_members.TryComp(ent, out var member) || member.Size.X <= 0 || member.Size.Y <= 0)
            return;
        var source = ent.Comp;
        source.Active = member.Connected;
        source.Grid = grid;
        source.Origin = _maps.TileIndicesFor(grid, mapGrid, xform.Coordinates);
        source.Size = member.Size;
        source.Rotation = RotGeometry.QuarterTurns(xform.LocalRotation);
        var opposite = source.Origin + RotGeometry.Rotate(member.Size - Vector2i.One, source.Rotation);
        var radius = Math.Clamp(source.Radius, 0, 16);
        source.Bounds = new Box2i(Math.Min(source.Origin.X, opposite.X) - radius, Math.Min(source.Origin.Y, opposite.Y) - radius,
            Math.Max(source.Origin.X, opposite.X) + radius, Math.Max(source.Origin.Y, opposite.Y) + radius);
        var index = EnsureComp<RotSpreadGridComponent>(grid);
        for (var x = source.Bounds.Left; x <= source.Bounds.Right; x++)
        {
            for (var y = source.Bounds.Bottom; y <= source.Bounds.Top; y++)
            {
                var cell = new Vector2i(x, y);
                if (!index.Watchers.TryGetValue(cell, out var watchers))
                    index.Watchers[cell] = watchers = [];
                watchers.Add(ent);
                source.Watched.Add(cell);
            }
        }
        source.Rebuild = true;
    }

    private void Unregister(Entity<RotSpreadComponent> ent)
    {
        CancelConversion(ent);
        var source = ent.Comp;
        if (source.Grid is { } grid && _grids.TryComp(grid, out var index))
        {
            foreach (var cell in source.Watched)
            {
                if (index.Watchers.TryGetValue(cell, out var watchers) && watchers.Remove(ent) && watchers.Count == 0)
                    index.Watchers.Remove(cell);
            }
        }
        source.Grid = null;
        source.Watched.Clear();
        source.Frontier.Clear();
        source.Seen.Clear();
    }

    public bool IsReserved(EntityUid grid, Vector2i tile) => _grids.TryComp(grid, out var index) && index.Reservations.ContainsKey(tile);

    private bool TryContext(Entity<RotSpreadComponent> ent, out Entity<RotIntelligentComponent, RotColonyStateComponent> core,
        out Entity<MapGridComponent> grid)
    {
        core = default;
        grid = default;
        if (TerminatingOrDeleted(ent) || !_members.TryComp(ent, out var member) || !member.Connected
            || member.Core is not { } uid || !_colony.IsActiveCore(uid)
            || !TryComp<RotIntelligentComponent>(uid, out var brain) || !brain.NetworkReady
            || !TryComp<RotColonyStateComponent>(uid, out var colony)
            || ent.Comp.Grid is not { } gridUid || gridUid != colony.Grid
            || !TryComp<MapGridComponent>(gridUid, out var mapGrid) || !Transform(ent).Anchored)
            return false;
        core = (uid, brain, colony);
        grid = (gridUid, mapGrid);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        LastWork = 0;
        LastGrowth = 0;
        _work.Clear();
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<RotSpreadComponent>();
        while (query.MoveNext(out var uid, out var source))
        {
            if (source.Active && source.Grid != null && now >= source.NextGrowth && (source.Rebuild || source.Frontier.Count > 0 || source.Target != null))
                _work.Add((uid, source));
        }
        var count = _work.Count;
        if (count == 0)
            return;
        var start = _cursor;
        for (var i = 0; i < count && LastWork < _budget && LastGrowth < _growthBudget; i++)
        {
            var index = (start + i) % count;
            var ent = _work[index];
            // Eligibility checks also consume budget so inactive colonies cannot create unbounded work.
            LastWork++;
            if (TryContext(ent, out var core, out var grid))
                Grow(ent, core, grid, Math.Min(16, _budget - LastWork));
            _cursor = (index + 1) % count;
        }
    }

    private void Grow(Entity<RotSpreadComponent> ent, Entity<RotIntelligentComponent, RotColonyStateComponent> core,
        Entity<MapGridComponent> grid, int budget)
    {
        var source = ent.Comp;
        if (source.Target != null && source.ConversionReady)
        {
            FinishConversion(ent, core, grid);
            return;
        }
        if (source.Rebuild)
        {
            source.Rebuild = false;
            source.Frontier.Clear();
            source.Seen.Clear();
            for (var x = 0; x < source.Size.X; x++)
                for (var y = 0; y < source.Size.Y; y++)
                    Enqueue(source, source.Origin + RotGeometry.Rotate(new Vector2i(x, y), source.Rotation));
        }
        // Obstacles under corrosion and walls waiting for the running conversion are retried after the rest of
        // the frontier, so a single table or wall never stalls growth into free tiles around it.
        _deferred.Clear();
        var grown = false;
        while (budget-- > 0 && LastGrowth < _growthBudget && source.Frontier.TryDequeue(out var tile))
        {
            LastWork++;
            var kind = Classify(ent, core, grid, tile, out var target);
            if (kind == RotGrowthCell.Blocked)
                continue;
            if (kind == RotGrowthCell.Existing)
            {
                ExpandFrontier(source, tile);
                continue;
            }
            if (kind == RotGrowthCell.Destructible && target is { } damageTarget)
            {
                Corrode(ent, damageTarget, core);
                _deferred.Add(tile);
                continue;
            }
            if (kind == RotGrowthCell.Convertible && source.Target != null)
            {
                _deferred.Add(tile);
                continue;
            }
            if (core.Comp2.Cells.Count >= core.Comp1.MaxTerritory)
            {
                source.Rebuild = true;
                _deferred.Clear();
                Delay(source);
                return;
            }
            if (target is { } obstacle)
                BeginConversion(ent, grid, tile, obstacle);
            else
            {
                var tissue = Spawn(source.Tissue, _maps.GridTileToLocal(grid, grid.Comp, tile));
                _colony.Join(tissue, core);
                _colony.CopyColonyStrain(core, tissue);
                ExpandFrontier(source, tile);
                LastGrowth++;
            }
            grown = true;
            break;
        }
        var passFinished = source.Frontier.Count == 0;
        foreach (var tile in _deferred)
            source.Frontier.Enqueue(tile);
        // Growth keeps its pace; a full pass over only obstacles waits instead of rescanning them every tick.
        if (grown || passFinished)
            Delay(source);
    }

    private void Delay(RotSpreadComponent source)
    {
        var interval = source.Interval > TimeSpan.Zero ? source.Interval : TimeSpan.FromSeconds(0.1);
        source.NextGrowth += interval;
        if (source.NextGrowth <= _timing.CurTime)
            source.NextGrowth = _timing.CurTime + interval;
    }

    private static void ExpandFrontier(RotSpreadComponent source, Vector2i tile)
    {
        foreach (var direction in Neighbors)
            Enqueue(source, tile + direction);
    }

    private static void Enqueue(RotSpreadComponent source, Vector2i tile)
    {
        if (tile.X >= source.Bounds.Left && tile.X <= source.Bounds.Right && tile.Y >= source.Bounds.Bottom
            && tile.Y <= source.Bounds.Top && source.Seen.Add(tile))
            source.Frontier.Enqueue(tile);
    }
}
