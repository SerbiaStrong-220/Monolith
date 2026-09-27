namespace Content.Shared._Mono.MAWC.Shields;

// Class attributes stay on the main declaration to avoid duplicate network generation.
public sealed partial class ShieldLinkSourceComponent
{
    /// <summary>How often to refresh nearby receivers while this source exists.</summary>
    [DataField]
    public TimeSpan LinkUpdateInterval = TimeSpan.FromSeconds(0.25);

    /// <summary>Next server-side range check. Paused sources retain their remaining delay.</summary>
    [AutoPausedField]
    public TimeSpan NextLinkUpdate;

    /// <summary>Reusable scratch set for a single source refresh; never sent to clients.</summary>
    public readonly HashSet<EntityUid> DesiredLinks = new();
}
