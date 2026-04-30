using System.Numerics;
using Content.Client._Mono.Radar;
using Content.Shared._Mono.Radar;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    private const float NebulaFTLFootprintSampleStep = 8f;
    private static readonly Color BlueNebulaRadarColor = new(0.38f, 0.70f, 1f, 0.85f);
    private static readonly Color RedNebulaRadarColor = new(1f, 0.30f, 0.26f, 0.85f);
    private static readonly Color PurpleNebulaRadarColor = new(0.73f, 0.40f, 1f, 0.85f);

    private bool CanFTLToNebulaPreview(EntityUid shuttleUid, MapGridComponent grid, EntityCoordinates targetCoordinates, Angle targetAngle)
    {
        var blips = _blips.GetCurrentNebulaMapBlips();

        if (EntManager.TryGetComponent(shuttleUid, out TransformComponent? xform))
        {
            var (currentOrigin, currentRotation) = _xformSystem.GetWorldPositionRotation(xform);
            if (DoesFTLFootprintHitBlockedNebula(grid.LocalAABB, currentOrigin, currentRotation, xform.MapID, blips))
                return false;
        }

        var targetMap = _xformSystem.ToMapCoordinates(targetCoordinates);
        if (targetMap == MapCoordinates.Nullspace)
            return true;

        if (!EntManager.TryGetComponent(shuttleUid, out PhysicsComponent? physics))
            return true;

        var targetOrigin = targetMap.Position - targetAngle.RotateVec(physics.LocalCenter);
        return !DoesFTLFootprintHitBlockedNebula(grid.LocalAABB, targetOrigin, targetAngle, targetMap.MapId, blips);
    }

    private bool DoesFTLFootprintHitBlockedNebula(
        Box2 localBounds,
        Vector2 targetOrigin,
        Angle targetAngle,
        MapId mapId,
        List<BlipData> blips)
    {
        if (DoesFTLFootprintContainBlockedNebulaCenter(localBounds, targetOrigin, targetAngle, mapId, blips))
            return true;

        if (IsLocalFTLPointInBlockedNebula(localBounds.Center, targetOrigin, targetAngle, mapId, blips))
            return true;

        if (IsLocalFTLPointInBlockedNebula(new Vector2(localBounds.Left, localBounds.Bottom), targetOrigin, targetAngle, mapId, blips) ||
            IsLocalFTLPointInBlockedNebula(new Vector2(localBounds.Left, localBounds.Top), targetOrigin, targetAngle, mapId, blips) ||
            IsLocalFTLPointInBlockedNebula(new Vector2(localBounds.Right, localBounds.Bottom), targetOrigin, targetAngle, mapId, blips) ||
            IsLocalFTLPointInBlockedNebula(new Vector2(localBounds.Right, localBounds.Top), targetOrigin, targetAngle, mapId, blips))
        {
            return true;
        }

        var horizontalSamples = Math.Max(1, (int) MathF.Ceiling(localBounds.Width / NebulaFTLFootprintSampleStep));
        for (var i = 1; i < horizontalSamples; i++)
        {
            var x = MathHelper.Lerp(localBounds.Left, localBounds.Right, i / (float) horizontalSamples);
            if (IsLocalFTLPointInBlockedNebula(new Vector2(x, localBounds.Bottom), targetOrigin, targetAngle, mapId, blips) ||
                IsLocalFTLPointInBlockedNebula(new Vector2(x, localBounds.Top), targetOrigin, targetAngle, mapId, blips))
            {
                return true;
            }
        }

        var verticalSamples = Math.Max(1, (int) MathF.Ceiling(localBounds.Height / NebulaFTLFootprintSampleStep));
        for (var i = 1; i < verticalSamples; i++)
        {
            var y = MathHelper.Lerp(localBounds.Bottom, localBounds.Top, i / (float) verticalSamples);
            if (IsLocalFTLPointInBlockedNebula(new Vector2(localBounds.Left, y), targetOrigin, targetAngle, mapId, blips) ||
                IsLocalFTLPointInBlockedNebula(new Vector2(localBounds.Right, y), targetOrigin, targetAngle, mapId, blips))
            {
                return true;
            }
        }

        return false;
    }

    private bool DoesFTLFootprintContainBlockedNebulaCenter(
        Box2 localBounds,
        Vector2 targetOrigin,
        Angle targetAngle,
        MapId mapId,
        List<BlipData> blips)
    {
        foreach (var blip in blips)
        {
            if (!IsBlockedNebulaBlip(blip))
                continue;

            var mapCoords = _xformSystem.ToMapCoordinates(blip.Position);
            if (mapCoords.MapId != mapId)
                continue;

            var localCenter = (-targetAngle).RotateVec(mapCoords.Position - targetOrigin);
            if (localBounds.Contains(localCenter))
                return true;
        }

        return false;
    }

    private bool IsLocalFTLPointInBlockedNebula(Vector2 localPoint, Vector2 targetOrigin, Angle targetAngle, MapId mapId, List<BlipData> blips)
    {
        var worldPoint = targetOrigin + targetAngle.RotateVec(localPoint);
        return IsPointInBlockedNebula(worldPoint, mapId, blips);
    }

    private bool IsPointInBlockedNebula(Vector2 worldPoint, MapId mapId, List<BlipData> blips)
    {
        foreach (var blip in blips)
        {
            if (!IsBlockedNebulaBlip(blip))
                continue;

            var mapCoords = _xformSystem.ToMapCoordinates(blip.Position);
            if (mapCoords.MapId != mapId)
                continue;

            if (IsPointInNebulaPolygon(worldPoint - mapCoords.Position, blip.Config))
                return true;
        }

        return false;
    }

    private static bool IsBlockedNebulaBlip(BlipData blip)
    {
        return blip.Config.Shape == RadarBlipShape.NebulaPolygon &&
               blip.Config.Points is { Count: >= 3 } &&
               (IsSameNebulaColor(blip.Config.Color, BlueNebulaRadarColor) ||
                IsSameNebulaColor(blip.Config.Color, RedNebulaRadarColor) ||
                IsSameNebulaColor(blip.Config.Color, PurpleNebulaRadarColor));
    }

    private static bool IsSameNebulaColor(Color a, Color b)
    {
        return MathHelper.CloseTo(a.R, b.R) &&
               MathHelper.CloseTo(a.G, b.G) &&
               MathHelper.CloseTo(a.B, b.B);
    }

    private static bool IsPointInNebulaPolygon(Vector2 localPoint, BlipConfig config)
    {
        if (config.Points == null)
            return false;

        var inside = false;
        var j = config.Points.Count - 1;

        for (var i = 0; i < config.Points.Count; i++)
        {
            var current = config.Points[i];
            var previous = config.Points[j];

            if ((current.Y > localPoint.Y) != (previous.Y > localPoint.Y) &&
                localPoint.X < (previous.X - current.X) * (localPoint.Y - current.Y) / (previous.Y - current.Y) + current.X)
            {
                inside = !inside;
            }

            j = i;
        }

        return inside;
    }
}
