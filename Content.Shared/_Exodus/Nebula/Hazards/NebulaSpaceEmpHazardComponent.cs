namespace Content.Shared._Exodus.Nebula.Hazards;

/// <summary>
/// Configures EMP pulses the nebula inflicts on free-space (EVA) entities inside it.
/// Sits on a nebula marker prototype; the EMP system reads it on each pulse via the entity's
/// <see cref="NebulaPresenceComponent.Marker"/>.
/// Decoupled from <see cref="NebulaEmpHazardComponent"/> so grid hazards and space hazards
/// can be enabled independently per marker.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaSpaceEmpHazardComponent : Component
{
    /// <summary>
    /// Master switch. When false the marker carries this configuration as documentation
    /// without actually pulsing EVA targets — useful to record tuned values for a future
    /// enablement without re-adding the component.
    /// </summary>
    [DataField]
    public bool Enabled = true;

    [DataField]
    public int MinStrikeDelaySeconds = 5;

    [DataField]
    public int MaxStrikeDelaySeconds = 30;

    [DataField]
    public float Range = 4f;

    [DataField]
    public float EnergyConsumption = 500000f;

    [DataField]
    public float DisableDuration = 5f;
}
