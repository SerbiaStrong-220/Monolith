using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    // Exodus-begin | nebula shield hazard absorption
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
