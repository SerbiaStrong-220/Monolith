namespace Content.Server._Mono.AlertLevel;

/// <summary>
/// Way of indicating if a round is before or after war declaration when you join.
/// </summary>
[RegisterComponent]
public sealed partial class WarLevelComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly)]
    public bool PostWar => Declarations.Count != 0; // Exodus: aggregate of active pairwise declarations.
}
