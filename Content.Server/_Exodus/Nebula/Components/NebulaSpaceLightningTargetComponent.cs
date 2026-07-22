using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula.Components;

/// <summary>
/// Runtime timer state for personal nebula lightning on a free-space (EVA) entity. Static
/// configuration is on the marker prototype's
/// <see cref="Content.Shared._Exodus.Nebula.Hazards.NebulaSpaceLightningHazardComponent"/>; this
/// component only stores per-entity timers and statistics for the current marker.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaSpaceLightningTargetComponent : Component
{
    [ViewVariables]
    public EntProtoId Marker;

    [ViewVariables]
    public TimeSpan NextStrike;

    [ViewVariables]
    public TimeSpan LastStrike;

    [ViewVariables]
    public int StrikeCount;
}
