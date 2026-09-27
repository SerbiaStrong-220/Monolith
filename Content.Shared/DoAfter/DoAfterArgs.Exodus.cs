namespace Content.Shared.DoAfter;

// Exodus - capability-scoped range and sound prediction for delayed interactions.
public sealed partial class DoAfterArgs
{
    /// <summary>
    /// Optional provider key for an extended DistanceThreshold. The provider must approve
    /// ValidateDoAfterRangeEvent on the user for the entire duration of the interaction.
    /// </summary>
    [DataField]
    public string? RangeProvider;

    /// <summary>False for server-only interactions whose actor cannot predict the completion sound.</summary>
    [DataField]
    public bool PredictSound = true;
}
