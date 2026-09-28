using Content.Shared._Exodus.Examine;
using System.Numerics;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Content.Shared.StationAi;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem
{
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    private readonly HashSet<Vector2i> _viewTiles = [];
    private readonly List<SpaceVisionSource> _crossGridVision = [];
    private readonly HashSet<RotVisionStamp> _visionInputs = [];
    private readonly HashSet<Entity<OccluderComponent>> _visionBlockers = [];

    private void OnVisionRequest(RotVisionRequestEvent args, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is { } core && TryComp<RotColonyStateComponent>(core, out var state))
            state.ViewFrame = null;
    }

    private void OnRemoteExamine(Entity<RotIntelligentComponent> ent, ref RemoteExamineEvent args)
    {
        if (!ent.Comp.Rooted)
            return;
        args.Handled = true;
        args.Allowed = (args.Examined is not { } target || !_containers.IsEntityInContainer(target))
            && CanSee(ent, args.Target);
    }

    public bool CanSee(EntityUid core, EntityCoordinates target) => target.IsValid(EntityManager)
        && CanSee(core, _transform.ToMapCoordinates(target));

    public bool CanSee(EntityUid core, MapCoordinates target)
    {
        if (!IsActiveCore(core) || !_colonyQuery.TryComp(core, out var state)
            || _transform.GetMapCoordinates(core).MapId != target.MapId)
            return false;
        foreach (var uid in state.Members)
        {
            if (_members.TryComp(uid, out var member) && member.VisionRange > 0)
                RefreshVision((uid, member));
        }
        if (_mapManager.TryFindGridAt(target, out var grid, out var mapGrid) && TryComp<BroadphaseComponent>(grid, out var broadphase))
        {
            var local = _transform.ToCoordinates(grid, target);
            var tile = _maps.TileIndicesFor(grid, mapGrid, local);
            if (_vision.IsAccessible((grid, broadphase, mapGrid), tile, expansionSize: MaximumVisionRange(state) + 1, network: core))
                return true;
            return CanSeeSpace(state, target, grid);
        }
        return CanSeeSpace(state, target);
    }

    private float MaximumVisionRange(RotColonyStateComponent state)
    {
        var range = 0f;
        foreach (var uid in state.Members)
        {
            if (_members.TryComp(uid, out var member) && member.Connected)
                range = MathF.Max(range, member.VisionRange);
        }
        return range;
    }

    private bool CanSeeSpace(RotColonyStateComponent state, MapCoordinates target, EntityUid? excludeGrid = null,
        List<SpaceVisionSource>? sources = null)
    {
        if (sources != null)
        {
            foreach (var source in sources)
            {
                if (SourceSeesSpace(source, target))
                    return true;
            }
            return false;
        }
        foreach (var uid in state.Members)
        {
            if (TryGetSpaceVisionSource(uid, excludeGrid, out var source) && SourceSeesSpace(source, target))
                return true;
        }
        return false;
    }

    private bool TryGetSpaceVisionSource(EntityUid uid, EntityUid? excludeGrid, out SpaceVisionSource source)
    {
        source = default;
        if (!_visionQuery.TryComp(uid, out var vision) || !vision.Enabled
            || !_transforms.TryComp(uid, out var xform) || excludeGrid != null && xform.GridUid == excludeGrid)
            return false;

        source = new SpaceVisionSource(uid, _transform.GetMapCoordinates(uid, xform), vision.Range);
        return true;
    }

    private bool SourceSeesSpace(SpaceVisionSource source, MapCoordinates target)
    {
        if (source.Coordinates.MapId != target.MapId)
            return false;

        var offset = target.Position - source.Coordinates.Position;
        var distance = offset.Length();
        if (distance > source.Range)
            return false;
        if (MathHelper.CloseTo(distance, 0))
            return true;

        // Only obstruction matters. The native ray query ignores the source without a closure
        // and stops collecting hits after the first opaque fixture.
        var ray = new CollisionRay(source.Coordinates.Position, offset / distance, (int)CollisionGroup.Opaque);
        foreach (var _ in _physics.IntersectRay(target.MapId, ray, distance, source.Entity, returnOnFirstHit: true))
        {
            return false;
        }

        return true;
    }

    private void UpdateVision(Entity<RotIntelligentComponent, RotColonyStateComponent> ent)
    {
        if (!TryComp<ActorComponent>(ent, out var actor))
            return;
        var eye = HasComp<PilotComponent>(ent) ? ent.Owner : ent.Comp1.Eye;
        if (eye is not { } camera || TerminatingOrDeleted(camera))
            return;
        foreach (var uid in ent.Comp2.Members)
        {
            if (_members.TryComp(uid, out var member) && member.VisionRange > 0)
                RefreshVision((uid, member));
        }
        var origin = _transform.GetMapCoordinates(camera);
        var cameraTransform = Transform(camera);
        var frame = cameraTransform.GridUid ?? cameraTransform.MapUid;
        if (frame == null)
            return;
        var localOrigin = _transform.ToCoordinates(frame.Value, origin).Position;
        var viewCenter = new Vector2i((int)MathF.Floor(localOrigin.X), (int)MathF.Floor(localOrigin.Y));
        CollectVisionInputs(ent.Comp2, frame.Value, origin);
        if (ent.Comp2.ViewFrame == frame && ent.Comp2.ViewCenter == viewCenter && ent.Comp2.VisionInputs.SetEquals(_visionInputs))
            return;
        ent.Comp2.ViewCenter = viewCenter;
        ent.Comp2.VisionInputs.Clear();
        ent.Comp2.VisionInputs.UnionWith(_visionInputs);
        _crossGridVision.Clear();
        foreach (var input in _visionInputs)
        {
            var uid = input.Source;
            if (input.Range <= 0)
                continue;
            if (TryGetSpaceVisionSource(uid, frame, out var source))
                _crossGridVision.Add(source);
        }
        _viewTiles.Clear();
        if (frame is { } grid && TryComp<MapGridComponent>(grid, out var mapGrid) && TryComp<BroadphaseComponent>(grid, out var broadphase))
        {
            var bounds = new Box2Rotated(Box2.CenteredAround(origin.Position, new Vector2(36, 36)), _transform.GetWorldRotation(grid));
            _vision.GetView((grid, broadphase, mapGrid), bounds, _viewTiles,
                expansionSize: MaximumVisionRange(ent.Comp2) + 1, network: ent.Owner);
            var center = _maps.TileIndicesFor(grid, mapGrid, _transform.ToCoordinates(grid, origin));
            for (var x = -18; x <= 18 && _crossGridVision.Count > 0; x++)
            {
                for (var y = -18; y <= 18; y++)
                {
                    var tile = center + new Vector2i(x, y);
                    if (!_viewTiles.Contains(tile) && CanSeeSpace(ent.Comp2,
                            _transform.ToMapCoordinates(_maps.GridTileToLocal(grid, mapGrid, tile)), grid, _crossGridVision))
                        _viewTiles.Add(tile);
                }
            }
        }
        else if (frame != null)
        {
            // Space has no grid tiles. Use a map-aligned mask and the same opaque collision layer as lasers.
            var center = new Vector2i((int)MathF.Floor(origin.X), (int)MathF.Floor(origin.Y));
            for (var x = -18; x <= 18; x++)
            {
                for (var y = -18; y <= 18; y++)
                {
                    var tile = center + new Vector2i(x, y);
                    if (CanSeeSpace(ent.Comp2, new MapCoordinates(new Vector2(tile.X + 0.5f, tile.Y + 0.5f), origin.MapId), sources: _crossGridVision))
                        _viewTiles.Add(tile);
                }
            }
        }
        if (ent.Comp2.ViewFrame == frame && ent.Comp2.LastView.SetEquals(_viewTiles) && ent.Comp2.Revision != 0)
            return;
        ent.Comp2.ViewFrame = frame;
        ent.Comp2.LastView.Clear();
        ent.Comp2.LastView.UnionWith(_viewTiles);
        ent.Comp2.Revision++;
        RaiseNetworkEvent(new RotVisionEvent(GetNetEntity(ent), GetNetEntity(frame), new(_viewTiles), ent.Comp2.Revision), actor.PlayerSession);
    }

    private void CollectVisionInputs(RotColonyStateComponent state, EntityUid frame, MapCoordinates origin)
    {
        _visionInputs.Clear();
        var maximumRange = 0f;
        foreach (var uid in state.Members)
        {
            if (!_visionQuery.TryComp(uid, out var vision) || !vision.Enabled)
                continue;
            var source = _transform.GetMapCoordinates(uid);
            if (source.MapId != origin.MapId || Vector2.DistanceSquared(source.Position, origin.Position) > MathF.Pow(vision.Range + 28, 2))
                continue;
            var xform = Transform(uid);
            var local = xform.ParentUid == frame ? xform.LocalPosition : _transform.ToCoordinates(frame, source).Position;
            // Static sources on the observed grid change visibility only when crossing a tile.
            if (Transform(uid).GridUid == frame)
                local = new Vector2(MathF.Floor(local.X), MathF.Floor(local.Y));
            _visionInputs.Add(new(uid, new Box2(local, local), vision.Range));
            maximumRange = MathF.Max(maximumRange, vision.Range);
        }
        _visionBlockers.Clear();
        _lookup.GetEntitiesIntersecting(origin.MapId, Box2.CenteredAround(origin.Position, new Vector2((maximumRange + 28) * 2)),
            _visionBlockers, flags: LookupFlags.Static | LookupFlags.Approximate);
        var angle = _transform.GetWorldRotation(frame);
        foreach (var (uid, blocker) in _visionBlockers)
        {
            if (!blocker.Enabled)
                continue;
            var xform = Transform(uid);
            var local = xform.ParentUid == frame ? xform.LocalPosition
                : _transform.ToCoordinates(frame, _transform.GetMapCoordinates(uid, xform)).Position;
            var rotation = xform.ParentUid == frame ? xform.LocalRotation : _transform.GetWorldRotation(uid) - angle;
            var bounds = _lookup.GetAABBNoContainer(uid, local, rotation);
            _visionInputs.Add(new(uid, bounds, -1));
        }
    }

    private readonly record struct SpaceVisionSource(EntityUid Entity, MapCoordinates Coordinates, float Range);
}
