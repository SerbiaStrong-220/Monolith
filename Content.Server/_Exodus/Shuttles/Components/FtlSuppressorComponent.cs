using Content.Server._Exodus.Shuttles.Systems;
using Content.Shared._Exodus.Shuttles;
using Robust.Shared.Audio;

namespace Content.Server._Exodus.Shuttles.Components;

/// <summary>
/// Projects a bluespace suppression field while anchored and powered.
/// No shuttle can start an FTL jump from inside the field and no shuttle can FTL into it.
/// </summary>
[RegisterComponent]
[Access(typeof(FtlSuppressorSystem))]
public sealed partial class FtlSuppressorComponent : Component
{
    /// <summary>
    /// Radius of the suppression field in meters.
    /// </summary>
    [DataField]
    public float Range = 1000f;

    /// <summary>
    /// Label of the suppression zone shown on the shuttle console FTL map.
    /// </summary>
    [DataField]
    public LocId ZoneName = "ftl-suppressor-zone-name";

    /// <summary>
    /// How the suppression zone is painted on the shuttle console FTL map.
    /// </summary>
    [DataField]
    public ShuttleExclusionFill ZoneFill = ShuttleExclusionFill.Hatched;

    /// <summary>
    /// How often an active suppressor checks whether its field moved far enough to resend it to open shuttle consoles.
    /// </summary>
    [DataField]
    public TimeSpan ZoneRefreshInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Distance in meters the field has to move before open shuttle consoles receive its new position.
    /// </summary>
    [DataField]
    public float ZoneRefreshDistance = 25f;

    /// <summary>
    /// Played on the consoles of a shuttle whose spooling jump was interrupted by this field.
    /// </summary>
    [DataField]
    public SoundSpecifier? AbortSound = new SoundPathSpecifier("/Audio/Machines/custom_deny.ogg");
}
