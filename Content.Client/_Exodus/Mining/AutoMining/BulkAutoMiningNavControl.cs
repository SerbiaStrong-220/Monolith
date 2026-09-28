using System.Numerics;
using Content.Client.Shuttles.UI;
using Content.Shared._Exodus.Mining.AutoMining;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using RadarBlipData = Content.Client._Mono.Radar.BlipData;

namespace Content.Client._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningNavControl : ShuttleNavControl
{
    [Dependency] private IMapManager _mapManager = default!;

    private readonly EntityQuery<MapGridComponent> _gridQuery;
    private readonly EntityQuery<TransformComponent> _xformQuery;

    private readonly SharedTransformSystem _transform;

    private List<Entity<MapGridComponent>> _gridPickBuffer = new();

    private static readonly Color SelectedGridColor = Color.FromHex("#FF8C00");

    private readonly List<NetEntity> _selectedGrids = new();
    private Vector2? _selectionStart;
    private bool _active;

    public event Action<NetEntity?>? OnGridSelected;

    public BulkAutoMiningNavControl() : base(64f, 512f, 512f)
    {
        IoCManager.InjectDependencies(this);
        _gridQuery = EntManager.GetEntityQuery<MapGridComponent>();
        _xformQuery = EntManager.GetEntityQuery<TransformComponent>();

        _transform = EntManager.System<SharedTransformSystem>();
        DefaultCursorShape = Control.CursorShape.Hand;
    }

    public void UpdateMiningState(BulkAutoMiningBoundUserInterfaceState state)
    {
        _selectedGrids.Clear();
        foreach (var target in state.SelectedTargets)
            _selectedGrids.Add(target.Grid);

        _active = state.Active;
        UpdateState(state.NavState);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        base.MeasureOverride(availableSize);
        // The base map requests all offered space; this pane must also leave room for its siblings.
        return MinSize;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            _selectionStart = args.RelativePosition;
            args.Handle();
            return;
        }

        base.KeyBindDown(args);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIClick)
        {
            if (!_active && _selectionStart is { } start && Vector2.DistanceSquared(start, args.RelativePosition) <= 36)
                OnGridSelected?.Invoke(TryPickGrid(args.RelativePosition));
            _selectionStart = null;
            args.Handle();
            return;
        }

        base.KeyBindUp(args);
    }

    protected override void DrawAdditionalOverlays(DrawingHandleScreen handle, Matrix3x2 worldToView, MapId mapId,
        List<RadarBlipData> blips, EntityUid? ownGrid)
    {
        base.DrawAdditionalOverlays(handle, worldToView, mapId, blips, ownGrid);
        DrawSelectedGrid(handle, worldToView, mapId);
    }

    private void DrawSelectedGrid(DrawingHandleScreen handle, Matrix3x2 worldToView, MapId mapId)
    {
        foreach (var netGrid in _selectedGrids)
        {
            if (!EntManager.TryGetEntity(netGrid, out var gridUid) ||
                gridUid is not { } gUid ||
                !_gridQuery.TryGetComponent(gUid, out var grid) ||
                !_xformQuery.TryComp(gUid, out var xform) || xform.MapID != mapId)
            {
                continue;
            }

            var gridToWorld = _transform.GetWorldMatrix(gUid);
            var gridToView = gridToWorld * worldToView;

            DrawGrid(handle, gridToView, (gUid, grid), SelectedGridColor, alpha: 0.25f);
        }
    }

    private NetEntity? TryPickGrid(Vector2 relativePosition)
    {
        if (_coordinates is not { } cord)
            return null;

        var mapPos = _transform.ToMapCoordinates(GetMouseEntityCoordinates(relativePosition));
        var tolerance = 4f / MinimapScale;
        var box = new Box2(mapPos.Position - new Vector2(tolerance), mapPos.Position + new Vector2(tolerance));
        _gridPickBuffer.Clear();
        _mapManager.FindGridsIntersecting(mapPos.MapId, box, ref _gridPickBuffer);

        EntityUid? ownGrid = null;
        if (_xformQuery.TryGetComponent(cord.EntityId, out var consoleXform))
            ownGrid = consoleXform.GridUid;

        EntityUid? best = null;
        var bestDist = float.MaxValue;

        foreach (var grid in _gridPickBuffer)
        {
            if (ownGrid != null && grid.Owner == ownGrid)
                continue;

            var local = Vector2.Transform(mapPos.Position, _transform.GetInvWorldMatrix(grid));
            var nearest = Vector2.Clamp(local, grid.Comp.LocalAABB.BottomLeft, grid.Comp.LocalAABB.TopRight);
            var dist = Vector2.DistanceSquared(local, nearest);
            if (dist >= bestDist)
                continue;

            bestDist = dist;
            best = grid.Owner;
        }

        return best is { } picked ? EntManager.GetNetEntity(picked) : null;
    }
}
