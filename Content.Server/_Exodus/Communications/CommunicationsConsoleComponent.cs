using Content.Shared._Exodus.Communications;
using Robust.Shared.Audio;

namespace Content.Server._Exodus.Communications;

[RegisterComponent]
public sealed partial class CommunicationsConsoleComponent : SharedCommunicationsConsoleComponent
{
    public float UIUpdateAccumulator = 0f;

    /// <summary>
    /// Remaining cooldown between making announcements.
    /// </summary>
    [DataField]
    public float AnnouncementCooldownRemaining;

    [DataField]
    public float BroadcastCooldownRemaining;

    /// <summary>
    /// Fluent ID for the announcement title
    /// If a Fluent ID isn't found, just uses the raw string
    /// </summary>
    [DataField(required: true)]
    public LocId Title = "comms-console-announcement-title-station";

    /// <summary>
    /// Announcement color
    /// </summary>
    [DataField]
    public Color Color = Color.Gold;

    /// <summary>
    /// Time in seconds between announcement delays on a per-console basis
    /// </summary>
    [DataField]
    public int Delay = 90;

    /// <summary>
    /// Time in seconds of announcement cooldown when a new console is created on a per-console basis
    /// </summary>
    [DataField]
    public int InitialDelay = 30;

    /// <summary>
    /// Exodus: can change the station alert level from this console.
    /// </summary>
    [DataField]
    public bool CanSetAlertLevel = true;

    /// <summary>
    /// Announce on all grids (for nukies)
    /// </summary>
    [DataField]
    public bool Global = false;

    /// <summary>
    /// Announce sound file path
    /// </summary>
    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Announcements/announce.ogg");
}

