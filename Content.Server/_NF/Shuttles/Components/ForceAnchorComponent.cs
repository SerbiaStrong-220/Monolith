namespace Content.Server._NF.Shuttles.Components;

/// <summary>
/// When on a grid, sets the grid to anchored and prevents further changes on init.
/// </summary>
[RegisterComponent]
public sealed partial class ForceAnchorComponent : Component
{
    /// <summary>When true, this grid is bedrock-anchored and cannot be released by gameplay systems.</summary>
    [DataField]
    public bool Bedrock; // Exodus: immutable bedrock anchor.
}
