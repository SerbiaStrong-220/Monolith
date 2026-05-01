using Robust.Shared.Audio;

namespace Content.Server._Exodus.Nebula;

[RegisterComponent]
public sealed partial class NebulaSpaceEmpTargetComponent : Component
{
    [DataField]
    public int MinPulseDelaySeconds = 5;

    [DataField]
    public int MaxPulseDelaySeconds = 30;

    [DataField]
    public float Range = 4f;

    [DataField]
    public float EnergyConsumption = 500000f;

    [DataField]
    public float DisableDuration = 5f;

    [ViewVariables]
    public TimeSpan NextPulse;

    [ViewVariables]
    public TimeSpan LastPulse;

    [ViewVariables]
    public int PulseCount;
}
