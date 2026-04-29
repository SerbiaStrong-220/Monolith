using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    // Exodus-begin | nebula shield hazard absorption
    public bool TryAbsorbNebulaStrike(EntityUid grid, float load, out EntityUid shield)
    {
        shield = EntityUid.Invalid;

        if (!TryComp<ShipShieldedComponent>(grid, out var shielded) ||
            shielded.Source is not { } source ||
            !TryComp<ShipShieldEmitterComponent>(source, out var emitter))
        {
            return false;
        }

        shield = shielded.Shield;
        emitter.Damage += load;

        if (TryComp<ApcPowerReceiverComponent>(source, out var receiver))
            AdjustEmitterLoad(source, emitter, receiver);

        return true;
    }
    // Exodus-end
}
