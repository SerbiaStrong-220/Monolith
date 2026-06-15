namespace Content.Shared._Exodus.LifeInsurance.Components;

/// <summary>
/// Keeps a life insurance machine component operational during grid power outages by draining an
/// internal <see cref="Content.Shared.Power.Components.BatteryComponent"/>. Sized to outlast brief
/// blackouts (~15 minutes) so cloning is not interrupted by flickering power.
/// </summary>
[RegisterComponent]
public sealed partial class LifeInsuranceBackupBatteryComponent : Component
{
    /// <summary>
    /// Power draw from the internal battery while running on backup, in watts.
    /// </summary>
    [DataField]
    public float DrainRate = 50f;

    /// <summary>
    /// Multiplier applied to <see cref="DrainRate"/> when recharging the battery from grid power.
    /// </summary>
    [DataField]
    public float ChargeRateMultiplier = 2f;

    /// <summary>
    /// Whether the component is currently operational (grid power or remaining battery charge).
    /// </summary>
    [ViewVariables]
    public bool Operational;

    /// <summary>
    /// Whether the component is currently powered directly from the grid (false = on battery).
    /// </summary>
    [ViewVariables]
    public bool OnGridPower;
}
