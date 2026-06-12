using Content.Server.Power.EntitySystems;
using Content.Shared._Exodus.LifeInsurance;
using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared.Power.Components;

namespace Content.Server._Exodus.LifeInsurance;

/// <summary>
/// Keeps life insurance machine components alive through grid power outages by draining an internal
/// battery, and recharges that battery while grid power is available.
/// </summary>
public sealed class LifeInsuranceBackupBatterySystem : EntitySystem
{
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly BatterySystem _battery = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LifeInsuranceBackupBatteryComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var gridPowered = _power.IsPowered(uid);

            if (!TryComp<BatteryComponent>(uid, out var battery))
            {
                comp.OnGridPower = gridPowered;
                comp.Operational = gridPowered;
                continue;
            }

            if (gridPowered)
            {
                // Recharge the backup battery while on grid power.
                if (battery.CurrentCharge < battery.MaxCharge)
                    _battery.SetCharge(uid, MathF.Min(battery.MaxCharge, battery.CurrentCharge + comp.DrainRate * comp.ChargeRateMultiplier * frameTime), battery);

                comp.OnGridPower = true;
                comp.Operational = true;
            }
            else
            {
                // Run off the backup battery.
                if (battery.CurrentCharge > 0f)
                {
                    _battery.UseCharge(uid, comp.DrainRate * frameTime, battery);
                    comp.Operational = battery.CurrentCharge > 0f;
                }
                else
                {
                    comp.Operational = false;
                }

                comp.OnGridPower = false;
            }
        }
    }

    /// <summary>
    /// Whether the machine can currently run (grid power or remaining backup charge).
    /// Falls back to true if the entity has no backup battery component configured.
    /// </summary>
    public bool IsOperational(EntityUid uid)
    {
        if (TryComp<LifeInsuranceBackupBatteryComponent>(uid, out var comp))
            return comp.Operational;

        return _power.IsPowered(uid);
    }

    /// <summary>
    /// Builds the power/battery status reported to the console UI.
    /// </summary>
    public LifeInsuranceMachineStatus GetStatus(EntityUid uid, bool connected)
    {
        var status = new LifeInsuranceMachineStatus { Connected = connected };

        if (TryComp<LifeInsuranceBackupBatteryComponent>(uid, out var comp))
            status.OnGridPower = comp.OnGridPower;
        else
            status.OnGridPower = _power.IsPowered(uid);

        if (TryComp<BatteryComponent>(uid, out var battery) && battery.MaxCharge > 0f)
            status.BatteryPercent = Math.Clamp(battery.CurrentCharge / battery.MaxCharge, 0f, 1f);

        return status;
    }
}
