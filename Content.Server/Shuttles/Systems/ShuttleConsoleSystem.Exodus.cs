using System.Numerics;
using Content.Server._Exodus.Nebula;
using Content.Shared._Exodus.Nebula;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    private const float NebulaFTLFootprintSampleStep = 8f;

    [Dependency] private readonly NebulaPresenceSystem _nebulaPresence = default!;

    private bool CanFTLToNebula(EntityUid shuttleUid, EntityCoordinates targetCoordinates, Angle targetAngle)
    {
        var mapCoords = _transform.ToMapCoordinates(targetCoordinates);
        if (mapCoords == MapCoordinates.Nullspace ||
            !_mapManager.MapExists(mapCoords.MapId))
        {
            return true;
        }

        var mapUid = _mapManager.GetMapEntityId(mapCoords.MapId);
        if (!TryComp<NebulaMapComponent>(mapUid, out var nebulaMap) || nebulaMap.Nebulas.Count == 0)
            return true;

        if (IsBlockedNebulaAt(mapCoords.Position, nebulaMap))
            return false;

        if (!TryComp<MapGridComponent>(shuttleUid, out var grid) ||
            !TryComp<PhysicsComponent>(shuttleUid, out var physics))
        {
            return true;
        }

        var targetOrigin = mapCoords.Position - targetAngle.RotateVec(physics.LocalCenter);
        return !DoesFootprintHitBlockedNebula(grid.LocalAABB, targetOrigin, targetAngle, nebulaMap);
    }

    private bool DoesFootprintHitBlockedNebula(Box2 localBounds, Vector2 targetOrigin, Angle targetAngle, NebulaMapComponent nebulaMap)
    {
        if (DoesFootprintContainBlockedNebulaCenter(localBounds, targetOrigin, targetAngle, nebulaMap))
            return true;

        if (IsLocalPointInBlockedNebula(localBounds.Center, targetOrigin, targetAngle, nebulaMap))
            return true;

        if (IsLocalPointInBlockedNebula(new Vector2(localBounds.Left, localBounds.Bottom), targetOrigin, targetAngle, nebulaMap) ||
            IsLocalPointInBlockedNebula(new Vector2(localBounds.Left, localBounds.Top), targetOrigin, targetAngle, nebulaMap) ||
            IsLocalPointInBlockedNebula(new Vector2(localBounds.Right, localBounds.Bottom), targetOrigin, targetAngle, nebulaMap) ||
            IsLocalPointInBlockedNebula(new Vector2(localBounds.Right, localBounds.Top), targetOrigin, targetAngle, nebulaMap))
        {
            return true;
        }

        var horizontalSamples = Math.Max(1, (int) MathF.Ceiling(localBounds.Width / NebulaFTLFootprintSampleStep));
        for (var i = 1; i < horizontalSamples; i++)
        {
            var x = MathHelper.Lerp(localBounds.Left, localBounds.Right, i / (float) horizontalSamples);
            if (IsLocalPointInBlockedNebula(new Vector2(x, localBounds.Bottom), targetOrigin, targetAngle, nebulaMap) ||
                IsLocalPointInBlockedNebula(new Vector2(x, localBounds.Top), targetOrigin, targetAngle, nebulaMap))
            {
                return true;
            }
        }

        var verticalSamples = Math.Max(1, (int) MathF.Ceiling(localBounds.Height / NebulaFTLFootprintSampleStep));
        for (var i = 1; i < verticalSamples; i++)
        {
            var y = MathHelper.Lerp(localBounds.Bottom, localBounds.Top, i / (float) verticalSamples);
            if (IsLocalPointInBlockedNebula(new Vector2(localBounds.Left, y), targetOrigin, targetAngle, nebulaMap) ||
                IsLocalPointInBlockedNebula(new Vector2(localBounds.Right, y), targetOrigin, targetAngle, nebulaMap))
            {
                return true;
            }
        }

        return false;
    }

    private bool DoesFootprintContainBlockedNebulaCenter(
        Box2 localBounds,
        Vector2 targetOrigin,
        Angle targetAngle,
        NebulaMapComponent nebulaMap)
    {
        for (var i = 0; i < nebulaMap.Nebulas.Count; i++)
        {
            if (!IsFTLBlockedNebulaType(NebulaTypeHelpers.GetOrDefault(nebulaMap.NebulaTypes, i)))
                continue;

            var localCenter = (-targetAngle).RotateVec(nebulaMap.Nebulas[i].Center - targetOrigin);
            if (localBounds.Contains(localCenter))
                return true;
        }

        return false;
    }

    private bool IsLocalPointInBlockedNebula(Vector2 localPoint, Vector2 targetOrigin, Angle targetAngle, NebulaMapComponent nebulaMap)
    {
        var worldPoint = targetOrigin + targetAngle.RotateVec(localPoint);
        return IsBlockedNebulaAt(worldPoint, nebulaMap);
    }

    private bool IsBlockedNebulaAt(Vector2 position, NebulaMapComponent nebulaMap)
    {
        return _nebulaPresence.TryGetNebulaAt(position, nebulaMap, out _, out var type, out _, out _) &&
               IsFTLBlockedNebulaType(type);
    }

    private static bool IsFTLBlockedNebulaType(NebulaType type)
    {
        return type is NebulaType.Blue or NebulaType.Red or NebulaType.Purple;
    }
}
