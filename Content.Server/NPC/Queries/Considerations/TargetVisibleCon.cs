// (c) Space Exodus Team - MPL-2.0 with CLA
// Authors: Lokilife
namespace Content.Server.NPC.Queries.Considerations;

/// <summary>
/// Returns whether the target is visible based on stealth visibility threshold.
/// Entities without stealth are always visible (returns 1f).
/// Entities with stealth visibility below threshold return 0f, otherwise 1f.
/// </summary>
public sealed partial class TargetVisibleCon : UtilityConsideration
{
    /// <summary>
    /// Visibility threshold for detection. Targets with visibility below this are not detected.
    /// </summary>
    [DataField("visibilityThreshold")]
    public float VisibilityThreshold = 0.5f;
}
