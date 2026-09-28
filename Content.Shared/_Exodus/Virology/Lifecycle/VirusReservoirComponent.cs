using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Virology.Lifecycle;

/// <summary>A persistent environmental source. Overlapping sources share one exposure roll.</summary>
[RegisterComponent]
public sealed partial class VirusReservoirComponent : Component
{
    [DataField]
    public VirusDescriptor? Strain;

    [DataField]
    public EntProtoId? InitialVirus;

    [DataField]
    public float Range = 2f;

    /// <summary>Optional anchored footprint. Exposure starts at its accessible perimeter instead of inside a wall.</summary>
    [DataField]
    public Vector2i Footprint;

    /// <summary>Chance per ten-second exposure window, before clothing and internals.</summary>
    [DataField]
    public float InfectionChance = 0.3f;

    [ViewVariables]
    public string? Identity;
}
