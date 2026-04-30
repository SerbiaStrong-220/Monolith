using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using System.Numerics;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    // Exodus-begin | nebula shield hazard absorption
    public bool TryAbsorbNebulaPointStrike(
        Entity<MapGridComponent, TransformComponent> grid,
        MapCoordinates point,
        float loadWatts,
        out EntityUid shield)
    {
        shield = EntityUid.Invalid;

        if (grid.Comp2.MapID != point.MapId ||
            !TryComp<ShipShieldedComponent>(grid.Owner, out var shielded))
        {
            return false;
        }

        var padding = TryComp<ShipShieldVisualsComponent>(shielded.Shield, out var visuals)
            ? visuals.Padding
            : 0f;

        var localPoint = Vector2.Transform(point.Position, _transformSystem.GetInvWorldMatrix(grid.Comp2));
        var center = grid.Comp1.LocalAABB.Center;
        var halfWidth = (grid.Comp1.LocalAABB.Width + padding) * 0.5f;
        var halfHeight = (grid.Comp1.LocalAABB.Height + padding) * 0.5f;

        if (halfWidth <= 0f || halfHeight <= 0f)
            return false;

        var dx = (localPoint.X - center.X) / halfWidth;
        var dy = (localPoint.Y - center.Y) / halfHeight;
        if (dx * dx + dy * dy > 1f)
            return false;

        return TryAbsorbNebulaStrike(grid.Owner, loadWatts, out shield);
    }

    public bool TryAbsorbNebulaStrike(EntityUid grid, float loadWatts, out EntityUid shield)
    {
        shield = EntityUid.Invalid;

        if (!TryComp<ShipShieldedComponent>(grid, out var shielded) ||
            shielded.Source is not { } source ||
            !TryComp<ShipShieldEmitterComponent>(source, out var emitter))
        {
            return false;
        }

        shield = shielded.Shield;
        // Convert nebula watt load into the shield's existing Damage accumulator.
        // Projectile deflection and normal shield recovery stay on the original code path.
        var currentLoad = CalculateLoadDamage(emitter);
        var targetLoad = Math.Clamp(currentLoad + loadWatts, 0f, emitter.MaxDraw);
        emitter.Damage = Math.Max(emitter.Damage, DamageForLoad(emitter, targetLoad));
        // Avoid the regular shield recovery tick immediately eating the same nebula strike.
        emitter.Accumulator = 0f;

        if (TryComp<ApcPowerReceiverComponent>(source, out var receiver))
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
