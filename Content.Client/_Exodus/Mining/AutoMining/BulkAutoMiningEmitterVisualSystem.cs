using System.Numerics;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Power;
using Robust.Client.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningEmitterVisualSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;

    private EntityQuery<TransformComponent> _transforms;
    private EntityQuery<MapGridComponent> _grids;

    public override void Initialize()
    {
        base.Initialize();
        _transforms = GetEntityQuery<TransformComponent>();
        _grids = GetEntityQuery<MapGridComponent>();
        SubscribeLocalEvent<BulkAutoMiningEmitterComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<BulkAutoMiningEmitterComponent, AfterAutoHandleStateEvent>(OnState);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<BulkAutoMiningEmitterComponent, SpriteComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var emitter, out var sprite, out var xform))
        {
            if (!TryGetBeam((uid, emitter, xform), out _, out _, out var rotation) &&
                !TryGetLinkBeam((uid, emitter, xform), out _, out _, out rotation))
                continue;

            // Only the head turns. The anchored body, square fixture and pipe port stay on the ship.
            _sprite.LayerSetRotation((uid, sprite), BulkAutoMiningVisualLayers.Head,
                rotation - _transform.GetWorldRotation(xform) - sprite.Rotation);
        }
    }

    /// <summary>Uses live grid transforms so the head and its beam track moving ships together.</summary>
    public bool TryGetBeam(Entity<BulkAutoMiningEmitterComponent, TransformComponent> ent,
        out Vector2 origin, out Vector2 target, out Angle rotation)
    {
        origin = target = default;
        rotation = default;
        if (ent.Comp1.BeamGrid is not { } targetGrid || ent.Comp2.MapUid == null ||
            !_grids.TryComp(targetGrid, out var grid) ||
            !_transforms.TryComp(targetGrid, out var targetXform) || targetXform.MapUid != ent.Comp2.MapUid)
            return false;

        var pivot = _transform.GetWorldPosition(ent.Comp2);
        target = _map.GridTileToWorldPos(targetGrid, grid, ent.Comp1.BeamTile);
        return TryGetBeamOrigin(pivot, target, ent.Comp1.MuzzleOffset, out origin, out rotation);
    }

    /// <summary>
    /// Consortium link between the muzzles of two linked lasers. The partner's pivot comes from its grid transform,
    /// so the beam is drawn even when the partner laser is outside the viewer's PVS.
    /// </summary>
    public bool TryGetLinkBeam(Entity<BulkAutoMiningEmitterComponent, TransformComponent> ent,
        out Vector2 origin, out Vector2 target, out Angle rotation)
    {
        origin = target = default;
        rotation = default;
        if (ent.Comp1.LinkPartner == null || ent.Comp1.LinkGrid is not { } linkGrid || ent.Comp2.MapUid == null ||
            !_transforms.TryComp(linkGrid, out var gridXform) || gridXform.MapUid != ent.Comp2.MapUid)
            return false;

        var pivot = _transform.GetWorldPosition(ent.Comp2);
        var partner = Vector2.Transform(ent.Comp1.LinkPosition, _transform.GetWorldMatrix(gridXform));
        return TryGetLinkEnds(pivot, partner, ent.Comp1.MuzzleOffset, out origin, out target, out rotation);
    }

    /// <summary>Radar variant of <see cref="TryGetLinkBeam"/> built from blip coordinates.</summary>
    public bool TryGetRadarLinkBeam(EntityCoordinates coordinates, BulkAutoMiningRadarBeam beam, MapId mapId,
        out Vector2 origin, out Vector2 target)
    {
        origin = target = default;
        var targetCoordinates = GetCoordinates(beam.Target);
        if (!coordinates.IsValid(EntityManager) || !targetCoordinates.IsValid(EntityManager))
            return false;

        var pivot = _transform.ToMapCoordinates(coordinates);
        var partner = _transform.ToMapCoordinates(targetCoordinates);
        return pivot.MapId == mapId && partner.MapId == mapId &&
               TryGetLinkEnds(pivot.Position, partner.Position, beam.MuzzleOffset, out origin, out target, out _);
    }

    private static bool TryGetLinkEnds(Vector2 pivot, Vector2 partner, float muzzleOffset,
        out Vector2 origin, out Vector2 target, out Angle rotation)
    {
        target = default;
        if (!TryGetBeamOrigin(pivot, partner, muzzleOffset, out origin, out rotation))
            return false;

        // Both heads face each other, so the beam ends at the partner's lens.
        return TryGetBeamOrigin(partner, pivot, muzzleOffset, out target, out _);
    }

    /// <summary>Only grid transforms are needed; the laser entity may be outside the viewer's PVS.</summary>
    public bool TryGetRadarBeam(EntityCoordinates coordinates, BulkAutoMiningRadarBeam beam, MapId mapId,
        out Vector2 origin, out Vector2 target)
    {
        origin = target = default;
        var targetCoordinates = GetCoordinates(beam.Target);
        if (!coordinates.IsValid(EntityManager) || !targetCoordinates.IsValid(EntityManager))
            return false;

        var pivot = _transform.ToMapCoordinates(coordinates);
        var destination = _transform.ToMapCoordinates(targetCoordinates);
        if (pivot.MapId != mapId || destination.MapId != mapId)
            return false;

        target = destination.Position;
        return TryGetBeamOrigin(pivot.Position, target, beam.MuzzleOffset, out origin, out _);
    }

    private static bool TryGetBeamOrigin(Vector2 pivot, Vector2 target, float muzzleOffset,
        out Vector2 origin, out Angle rotation)
    {
        origin = default;
        rotation = default;
        var delta = target - pivot;
        var distance = delta.Length();
        if (distance < 0.01f)
            return false;

        // RSI heads face south at zero rotation, matching ToWorldAngle's convention.
        rotation = delta.ToWorldAngle();
        origin = pivot + delta / distance * Math.Clamp(muzzleOffset, 0, distance);
        return true;
    }

    private void OnAppearanceChange(Entity<BulkAutoMiningEmitterComponent> ent, ref AppearanceChangeEvent args)
    {
        UpdateSprite(ent);
    }

    private void OnState(Entity<BulkAutoMiningEmitterComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite(ent);
    }

    private void UpdateSprite(Entity<BulkAutoMiningEmitterComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        _appearance.TryGetData<bool>(ent, PowerDeviceVisuals.Powered, out var powered);
        var firing = ent.Comp.BeamGrid != null || ent.Comp.LinkPartner != null;
        var state = !powered ? "off" : firing ? "mining" : "idle";
        _sprite.LayerSetRsiState((ent, sprite), BulkAutoMiningVisualLayers.Head, state);
    }
}

public enum BulkAutoMiningVisualLayers : byte
{
    Base,
    Head,
}
