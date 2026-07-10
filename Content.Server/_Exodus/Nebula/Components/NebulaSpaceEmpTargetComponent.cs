using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula.Components;

/// <summary>
/// Runtime timer state for personal nebula EMP pulses on a free-space (EVA) entity. Static
/// configuration is on the marker prototype's
/// <see cref="Content.Shared._Exodus.Nebula.Hazards.NebulaSpaceEmpHazardComponent"/>; this component
/// only stores per-entity timers and statistics for the current marker.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaSpaceEmpTargetComponent : Component
{
    [ViewVariables]
    public EntProtoId Marker;

    [ViewVariables]
    public TimeSpan NextPulse;

    [ViewVariables]
    public TimeSpan LastPulse;

    [ViewVariables]
    public int PulseCount;
}
