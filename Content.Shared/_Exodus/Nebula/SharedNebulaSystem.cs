using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Common API for asking nebula questions on either side. Concrete implementations live in
/// <c>Content.Server</c> and <c>Content.Client</c> and only differ in where they read the
/// nebula shape list from.
/// </summary>
public abstract class SharedNebulaSystem : EntitySystem
{
    [Dependency] protected readonly IMapManager MapManager = default!;
    [Dependency] protected readonly SharedTransformSystem TransformSystem = default!;

    private const float FTLFootprintSampleStep = 8f;

    public const string FTLRejectionAtSource = "shuttle-ftl-nebula-source";
    public const string FTLRejectionAtTarget = "shuttle-ftl-nebula";

    /// <summary>
    /// True if the shuttle can FTL from its current position to the given target.
    /// On false, <paramref name="rejection"/> is a localization id describing why.
    /// </summary>
    public bool CanFTL(EntityUid shuttleUid, EntityCoordinates targetCoordinates, Angle targetAngle, out string rejection)
    {
        rejection = string.Empty;

        if (!TryComp<MapGridComponent>(shuttleUid, out var grid) ||
            !TryComp<PhysicsComponent>(shuttleUid, out var physics) ||
            !TryComp<TransformComponent>(shuttleUid, out var xform))
        {
            return true;
        }

        var (currentOrigin, currentRotation) = TransformSystem.GetWorldPositionRotation(xform);
        if (DoesFootprintHitFTLBlocker(grid.LocalAABB, currentOrigin, currentRotation, xform.MapID))
        {
            rejection = FTLRejectionAtSource;
            return false;
        }

        var mapCoords = TransformSystem.ToMapCoordinates(targetCoordinates);
        if (mapCoords == MapCoordinates.Nullspace || !MapManager.MapExists(mapCoords.MapId))
            return true;

        var targetOrigin = mapCoords.Position - targetAngle.RotateVec(physics.LocalCenter);
        if (DoesFootprintHitFTLBlocker(grid.LocalAABB, targetOrigin, targetAngle, mapCoords.MapId))
        {
            rejection = FTLRejectionAtTarget;
            return false;
        }

        return true;
    }

    /// <summary>
    /// True if any sampled point of the rotated shuttle footprint, or any nebula center
    /// inside it, lies in an FTL-blocking nebula on <paramref name="mapId"/>.
    /// </summary>
    public bool DoesFootprintHitFTLBlocker(Box2 localBounds, Vector2 origin, Angle rotation, MapId mapId)
    {
        if (!TryGetMapData(mapId, out var shapes, out var types))
            return false;

        if (DoesFootprintContainBlockerCenter(localBounds, origin, rotation, shapes, types))
            return true;

        if (IsFTLBlockerAt(LocalToWorld(localBounds.Center, origin, rotation), shapes, types))
            return true;

        if (IsFTLBlockerAt(LocalToWorld(localBounds.BottomLeft, origin, rotation), shapes, types) ||
            IsFTLBlockerAt(LocalToWorld(localBounds.TopLeft, origin, rotation), shapes, types) ||
            IsFTLBlockerAt(LocalToWorld(localBounds.BottomRight, origin, rotation), shapes, types) ||
            IsFTLBlockerAt(LocalToWorld(localBounds.TopRight, origin, rotation), shapes, types))
        {
            return true;
        }

        var horizontalSamples = Math.Max(1, (int) MathF.Ceiling(localBounds.Width / FTLFootprintSampleStep));
        for (var i = 1; i < horizontalSamples; i++)
        {
            var x = MathHelper.Lerp(localBounds.Left, localBounds.Right, i / (float) horizontalSamples);
            if (IsFTLBlockerAt(LocalToWorld(new Vector2(x, localBounds.Bottom), origin, rotation), shapes, types) ||
                IsFTLBlockerAt(LocalToWorld(new Vector2(x, localBounds.Top), origin, rotation), shapes, types))
            {
                return true;
            }
        }

        var verticalSamples = Math.Max(1, (int) MathF.Ceiling(localBounds.Height / FTLFootprintSampleStep));
        for (var i = 1; i < verticalSamples; i++)
        {
            var y = MathHelper.Lerp(localBounds.Bottom, localBounds.Top, i / (float) verticalSamples);
            if (IsFTLBlockerAt(LocalToWorld(new Vector2(localBounds.Left, y), origin, rotation), shapes, types) ||
                IsFTLBlockerAt(LocalToWorld(new Vector2(localBounds.Right, y), origin, rotation), shapes, types))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True if <paramref name="worldPosition"/> lies inside a nebula on the given map.
    /// Outputs the type of the first containing nebula even if it is not FTL-blocking.
    /// </summary>
    public bool TryGetNebulaAt(MapId mapId, Vector2 worldPosition, out NebulaType type)
    {
        type = default;
        if (!TryGetMapData(mapId, out var shapes, out var types))
            return false;

        for (var i = 0; i < shapes.Count; i++)
        {
            var shape = shapes[i];
            if ((worldPosition - shape.Center).LengthSquared() > shape.BoundingRadius * shape.BoundingRadius)
                continue;

            if (!shape.Contains(worldPosition))
                continue;

            type = NebulaTypeHelpers.GetOrDefault(types, i);
            return true;
        }

        return false;
    }

    private static bool IsFTLBlockerAt(Vector2 worldPosition, IReadOnlyList<NebulaShape> shapes, IReadOnlyList<NebulaType> types)
    {
        for (var i = 0; i < shapes.Count; i++)
        {
            var shape = shapes[i];
            if ((worldPosition - shape.Center).LengthSquared() > shape.BoundingRadius * shape.BoundingRadius)
                continue;

            if (!shape.Contains(worldPosition))
                continue;

            return IsFTLBlockedNebulaType(NebulaTypeHelpers.GetOrDefault(types, i));
        }

        return false;
    }

    private static bool DoesFootprintContainBlockerCenter(
        Box2 localBounds,
        Vector2 origin,
        Angle rotation,
        IReadOnlyList<NebulaShape> shapes,
        IReadOnlyList<NebulaType> types)
    {
        var inverseAngle = -rotation;
        for (var i = 0; i < shapes.Count; i++)
        {
            if (!IsFTLBlockedNebulaType(NebulaTypeHelpers.GetOrDefault(types, i)))
                continue;

            var localCenter = inverseAngle.RotateVec(shapes[i].Center - origin);
            if (localBounds.Contains(localCenter))
                return true;
        }

        return false;
    }

    private static Vector2 LocalToWorld(Vector2 localPoint, Vector2 origin, Angle rotation)
    {
        return origin + rotation.RotateVec(localPoint);
    }

    /// <summary>
    /// Implementation hook: returns the nebula shape and type lists for the given map.
    /// </summary>
    protected abstract bool TryGetMapData(
        MapId mapId,
        out IReadOnlyList<NebulaShape> shapes,
        out IReadOnlyList<NebulaType> types);

    public static bool IsFTLBlockedNebulaType(NebulaType type)
    {
        return type is NebulaType.Blue or NebulaType.Red or NebulaType.Purple;
    }
}
