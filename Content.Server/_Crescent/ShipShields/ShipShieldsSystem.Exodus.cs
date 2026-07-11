using System.Numerics;
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    // Exodus-begin | shield hit absorption events
    private void InitializeShieldHitAbsorption()
    {
        SubscribeLocalEvent<ShipShieldedComponent, ShipShieldHitAttemptEvent>(OnShipShieldHitAttempt);
    }

    private void OnShipShieldHitAttempt(EntityUid grid, ShipShieldedComponent shielded, ref ShipShieldHitAttemptEvent args)
    {
        if (args.Absorbed)
            return;

        if (!IsPointInsideShield(grid, shielded, args.Point))
            return;

        if (!TryApplyShieldLoad(shielded, args.LoadWatts))
            return;

        args.Absorbed = true;
    }

    private bool IsPointInsideShield(EntityUid grid, ShipShieldedComponent shielded, MapCoordinates point)
    {
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid) ||
            !_transformQuery.TryGetComponent(grid, out var xform) ||
            xform.MapID != point.MapId)
        {
            return false;
        }

        var padding = _shieldVisualsQuery.TryGetComponent(shielded.Shield, out var visuals)
            ? visuals.Padding
            : 0f;

        var localPoint = Vector2.Transform(point.Position, _transformSystem.GetInvWorldMatrix(xform));
        var center = mapGrid.LocalAABB.Center;
        var halfWidth = (mapGrid.LocalAABB.Width + padding) * 0.5f;
        var halfHeight = (mapGrid.LocalAABB.Height + padding) * 0.5f;

        if (halfWidth <= 0f || halfHeight <= 0f)
            return false;

        var dx = (localPoint.X - center.X) / halfWidth;
        var dy = (localPoint.Y - center.Y) / halfHeight;
        return dx * dx + dy * dy <= 1f;
    }

    private bool TryApplyShieldLoad(ShipShieldedComponent shielded, float loadWatts)
    {
        if (shielded.Source is not { } source ||
            !_shieldEmitterQuery.TryGetComponent(source, out var emitter))
        {
            return false;
        }

        // Convert added watt load into the emitter's existing Damage accumulator so it shares
        // the same recovery/overload logic as projectile deflection.
        var currentLoad = CalculateLoadDamage(emitter);
        var targetLoad = Math.Clamp(currentLoad + loadWatts, 0f, emitter.MaxDraw);
        emitter.Damage = Math.Max(emitter.Damage, DamageForLoad(emitter, targetLoad));
        // Avoid the regular shield recovery tick immediately eating the same strike.
        emitter.Accumulator = 0f;

        if (_apcPowerReceiverQuery.TryGetComponent(source, out var receiver))
            AdjustEmitterLoad(source, emitter, receiver);

        return true;
    }

    private static float DamageForLoad(ShipShieldEmitterComponent emitter, float loadWatts)
    {
        if (loadWatts <= 0f)
            return 0f;

        if (emitter.PowerModifier <= 0f || emitter.DamageExp <= 0f)
            return emitter.Damage;

        return MathF.Pow(loadWatts / emitter.PowerModifier, 1f / emitter.DamageExp);
    }
    // Exodus-end
}
