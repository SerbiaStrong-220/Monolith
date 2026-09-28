using Content.Shared._Exodus.Communications;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.Communications;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CommunicationsConsoleComponent : SharedCommunicationsConsoleComponent
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUiUpdate;

    /// <summary>Earliest time another announcement can be sent from this console.</summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextAnnouncementAt;

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

