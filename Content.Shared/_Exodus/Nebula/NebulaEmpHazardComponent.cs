namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Configures EMP pulses this nebula emits at grids and players inside it.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaEmpHazardComponent : Component
{
    [DataField]
    public int MinDelaySeconds = 5;

    [DataField]
    public int MaxDelaySeconds = 30;

    [DataField]
    public float Range = 8f;

    [DataField]
    public float EnergyConsumption = 500000f;

    [DataField]
    public float DisableDuration = 5f;

    [DataField]
    public float PlayerRange = 32f;
}
