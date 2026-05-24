using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Runtime state for nebula lightning hazards on a grid. Static configuration (intervals,
/// damage, sounds, etc.) is read from the marker prototype's
/// <see cref="Content.Shared._Exodus.Nebula.NebulaLightningHazardComponent"/>; this component
/// only stores per-grid timers and statistics.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaLightningGridHazardComponent : Component
{
    /// <summary>Marker prototype id of the nebula causing the hazard.</summary>
    [ViewVariables]
    public EntProtoId Marker;

    [ViewVariables]
    public bool TimersInitialized;

    [ViewVariables]
    public TimeSpan NextSmallStrike;

    [ViewVariables]
    public TimeSpan NextHeavyStrike;

    [ViewVariables]
    public TimeSpan NextSuperHeavyStrike;

    [ViewVariables]
    public TimeSpan LastSmallStrike;

    [ViewVariables]
    public TimeSpan LastHeavyStrike;

    [ViewVariables]
    public TimeSpan LastSuperHeavyStrike;

    [ViewVariables]
    public TimeSpan LastSmallDelta;

    [ViewVariables]
    public TimeSpan LastHeavyDelta;

    [ViewVariables]
    public TimeSpan LastSuperHeavyDelta;

    [ViewVariables]
    public int SmallStrikeCount;

    [ViewVariables]
    public int HeavyStrikeCount;

    [ViewVariables]
    public int SuperHeavyStrikeCount;
}
