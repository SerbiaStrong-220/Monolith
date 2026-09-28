namespace Content.Server.AlertLevel;

public sealed partial class AlertLevelDetail
{
    /// <summary>
    /// How long the previous sector code stays in force after a console selects this one.
    /// Zero applies the code immediately.
    /// </summary>
    [DataField]
    public TimeSpan TransitionDuration = TimeSpan.Zero;

    /// <summary>
    /// First announcement, played when the transition starts. The code has not changed yet.
    /// </summary>
    [DataField]
    public LocId? StartAnnouncement;

    /// <summary>
    /// Second announcement, played when this code actually takes effect.
    /// </summary>
    [DataField]
    public LocId? EndAnnouncement;
}
