using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Atmos;

/// <summary>
/// Displays the remaining gas supply on the inventory icon while the tank is in use.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GasTankFillIndicatorComponent : Component
{
    /// <summary>
    /// Nominal filling pressure in kPa at 20 degrees Celsius, used with the tank volume
    /// to calculate the amount of gas represented by a full bar.
    /// </summary>
    [DataField]
    public float NominalPressure = 1000f;

    /// <summary>
    /// Optional nominal gas supply in moles, overriding the pressure-based capacity.
    /// </summary>
    [DataField]
    public float? NominalMoles;

    /// <summary>
    /// Remaining supply as a whole percentage, clamped to 0-100.
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public byte FillLevel;

    /// <summary>
    /// Whether the tank pressure is below its internals warning threshold.
    /// </summary>
    [AutoNetworkedField, ViewVariables]
    public bool IsLowPressure;
}
