namespace Content.Shared._Exodus.Lathe;

/// <summary>Optional per-rating upgrade curves for lathes that do not use exponential part scaling.</summary>
[RegisterComponent]
public sealed partial class LathePartMultipliersComponent : Component
{
    /// <summary>Print time multiplier for each manipulator rating. Unlisted ratings give no bonus.</summary>
    [DataField]
    public Dictionary<int, float> PrintTimeMultipliers = new();

    /// <summary>Material cost multiplier for each matter bin rating. Unlisted ratings give no bonus.</summary>
    [DataField]
    public Dictionary<int, float> MaterialUseMultipliers = new();
}
