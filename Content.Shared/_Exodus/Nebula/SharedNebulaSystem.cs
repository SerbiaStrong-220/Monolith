using System.Numerics;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Generation;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Common API for asking nebula questions on either side. Concrete implementations live in
/// <c>Content.Server</c> and <c>Content.Client</c> and only differ in where they read the
/// nebula summary list from.
/// </summary>
public abstract partial class SharedNebulaSystem : EntitySystem
{
    [Dependency] protected SharedMapSystem MapSystem = default!;
    [Dependency] protected SharedTransformSystem TransformSystem = default!;

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
            !TryComp(shuttleUid, out TransformComponent? xform))
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
        if (mapCoords == MapCoordinates.Nullspace || !MapSystem.TryGetMap(mapCoords.MapId, out _))
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
    /// True if any sampled point of the rotated shuttle footprint, or any FTL-blocking nebula
    /// center inside it, lies in an FTL-blocking nebula on <paramref name="mapId"/>.
    /// </summary>
    public bool DoesFootprintHitFTLBlocker(Box2 localBounds, Vector2 origin, Angle rotation, MapId mapId)
    {
        var hasSummaries = TryGetSummaries(mapId, out var summaries);
        // Exodus-begin nebula-death-zone-ftl
        var hasWorldEnd = TryGetWorldEnd(mapId, out var worldEnd);
        // Exodus-end

        if (!hasSummaries && !hasWorldEnd)
            return false;

        if (hasSummaries && DoesFootprintContainBlockerCenter(localBounds, origin, rotation, summaries))
            return true;

        if (IsBlockedAt(LocalToWorld(localBounds.Center, origin, rotation), hasSummaries, summaries, hasWorldEnd, worldEnd))
            return true;

        if (IsBlockedAt(LocalToWorld(localBounds.BottomLeft, origin, rotation), hasSummaries, summaries, hasWorldEnd, worldEnd) ||
            IsBlockedAt(LocalToWorld(localBounds.TopLeft, origin, rotation), hasSummaries, summaries, hasWorldEnd, worldEnd) ||
            IsBlockedAt(LocalToWorld(localBounds.BottomRight, origin, rotation), hasSummaries, summaries, hasWorldEnd, worldEnd) ||
            IsBlockedAt(LocalToWorld(localBounds.TopRight, origin, rotation), hasSummaries, summaries, hasWorldEnd, worldEnd))
        {
            return true;
        }

        var horizontalSamples = Math.Max(1, (int) MathF.Ceiling(localBounds.Width / FTLFootprintSampleStep));
        for (var i = 1; i < horizontalSamples; i++)
        {
            var x = MathHelper.Lerp(localBounds.Left, localBounds.Right, i / (float) horizontalSamples);
            if (IsBlockedAt(LocalToWorld(new Vector2(x, localBounds.Bottom), origin, rotation), hasSummaries, summaries, hasWorldEnd, worldEnd) ||
                IsBlockedAt(LocalToWorld(new Vector2(x, localBounds.Top), origin, rotation), hasSummaries, summaries, hasWorldEnd, worldEnd))
            {
                return true;
            }
        }

        var verticalSamples = Math.Max(1, (int) MathF.Ceiling(localBounds.Height / FTLFootprintSampleStep));
        for (var i = 1; i < verticalSamples; i++)
        {
            var y = MathHelper.Lerp(localBounds.Bottom, localBounds.Top, i / (float) verticalSamples);
            if (IsBlockedAt(LocalToWorld(new Vector2(localBounds.Left, y), origin, rotation), hasSummaries, summaries, hasWorldEnd, worldEnd) ||
                IsBlockedAt(LocalToWorld(new Vector2(localBounds.Right, y), origin, rotation), hasSummaries, summaries, hasWorldEnd, worldEnd))
            {
                return true;
            }
        }

        return false;
    }

    // Exodus-begin nebula-death-zone-ftl
    /// <summary>
    /// Returns the world-end death zone shape for the map if it has been generated.
    /// </summary>
    protected bool TryGetWorldEnd(MapId mapId, out WorldEndNebulaShape worldEnd)
    {
        worldEnd = default;

        if (!MapSystem.TryGetMap(mapId, out var mapUid))
            return false;

        if (!TryComp<NebulaMapDataComponent>(mapUid, out var component) || !component.WorldEnd.IsGenerated)
            return false;

        worldEnd = component.WorldEnd;
        return true;
    }

    private static bool IsBlockedAt(
        Vector2 worldPos,
        bool hasSummaries,
        IReadOnlyList<NebulaSummary>? summaries,
        bool hasWorldEnd,
        WorldEndNebulaShape worldEnd)
    {
        if (hasSummaries && IsFTLBlockerAt(worldPos, summaries!))
            return true;
        if (hasWorldEnd && worldEnd.Contains(worldPos))
            return true;
        return false;
    }
    // Exodus-end

    /// <summary>
    /// True if <paramref name="worldPosition"/> lies inside a nebula on the given map.
    /// Outputs the matched summary so callers can dispatch on its <see cref="NebulaSummary.Marker"/>.
    /// </summary>
    public bool TryGetNebulaAt(MapId mapId, Vector2 worldPosition, out NebulaSummary summary)
    {
        summary = default;
        if (!TryGetSummaries(mapId, out var summaries))
            return false;

        for (var i = 0; i < summaries.Count; i++)
        {
            var candidate = summaries[i];
            if ((worldPosition - candidate.Shape.Center).LengthSquared() > candidate.Shape.BoundingRadius * candidate.Shape.BoundingRadius)
                continue;

            if (!candidate.Shape.Contains(worldPosition))
                continue;

            summary = candidate;
            return true;
        }

        return false;
    }

    private static bool IsFTLBlockerAt(Vector2 worldPosition, IReadOnlyList<NebulaSummary> summaries)
    {
        for (var i = 0; i < summaries.Count; i++)
        {
            var summary = summaries[i];
            if ((worldPosition - summary.Shape.Center).LengthSquared() > summary.Shape.BoundingRadius * summary.Shape.BoundingRadius)
                continue;

            if (!summary.Shape.Contains(worldPosition))
                continue;

            return summary.BlocksFTL;
        }

        return false;
    }

    private static bool DoesFootprintContainBlockerCenter(
        Box2 localBounds,
        Vector2 origin,
        Angle rotation,
        IReadOnlyList<NebulaSummary> summaries)
    {
        var inverseAngle = -rotation;
        for (var i = 0; i < summaries.Count; i++)
        {
            if (!summaries[i].BlocksFTL)
                continue;

            var localCenter = inverseAngle.RotateVec(summaries[i].Shape.Center - origin);
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
    /// Implementation hook: returns the nebula summary list for the given map.
    /// </summary>
    protected abstract bool TryGetSummaries(MapId mapId, out IReadOnlyList<NebulaSummary> summaries);
}
