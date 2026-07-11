namespace Content.Shared._Exodus.Nebula.Hazards;

/// <summary>
/// Configures EMP pulses this nebula emits at grids inside it. Mirrors the
/// <c>EnableSmall/EnableHeavy/EnableSuperHeavy</c> pattern of lightning hazards: tuning
/// values can live on the marker as documentation while <see cref="Enabled"/> gates the
/// effect in/out.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaEmpHazardComponent : Component
{
    /// <summary>
    /// Master switch. When false the marker still carries this configuration (for
    /// documentation / future enablement) but the hazard coordinator ignores it.
    /// </summary>
    [DataField]
    public bool Enabled = true;

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
}
