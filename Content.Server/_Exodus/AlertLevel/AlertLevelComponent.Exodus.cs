namespace Content.Server.AlertLevel;

public sealed partial class AlertLevelComponent
{
    /// <summary>
    /// Sector code that will replace <see cref="CurrentLevel"/> when the transition timer elapses.
    /// The current code stays in force until then.
    /// </summary>
    [ViewVariables]
    public string? PendingLevel;

    /// <summary>
    /// Game time at which <see cref="PendingLevel"/> becomes the active sector code.
    /// </summary>
    [ViewVariables]
    public TimeSpan PendingAt;

    public bool PendingPlaySound;
    public bool PendingAnnounce;
    public bool PendingLocked;
}
