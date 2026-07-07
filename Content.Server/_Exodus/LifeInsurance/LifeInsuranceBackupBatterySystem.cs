using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.LifeInsurance;
using Content.Shared.Power.Components;

namespace Content.Server._Exodus.LifeInsurance;

public sealed class LifeInsuranceBackupBatterySystem : EntitySystem
{
    [Dependency] private PowerReceiverSystem _power = default!;

    /// <summary>
    /// Is machine currently has power from the grid, or from its internal battery.
    /// </summary>
    public bool IsOperational(EntityUid uid)
    {
        return _power.IsPowered(uid);
    }

    /// <summary>
    /// Power/battery status reported to the console UI.
    /// </summary>
    public LifeInsuranceMachineStatus GetStatus(EntityUid uid, bool connected)
    {
        var status = new LifeInsuranceMachineStatus { Connected = connected };

        // No power, grid is down and battery is depleted.
        status.Unpowered = !_power.IsPowered(uid);

        // Running on grid power, otherwise running on battery.
        status.OnGridPower = !(TryComp<ApcPowerReceiverBatteryComponent>(uid, out var backup) && backup.Enabled);

        if (TryComp<BatteryComponent>(uid, out var battery) && battery.MaxCharge > 0f)
            status.BatteryPercent = Math.Clamp(battery.CurrentCharge / battery.MaxCharge, 0f, 1f);

        return status;
    }
}
