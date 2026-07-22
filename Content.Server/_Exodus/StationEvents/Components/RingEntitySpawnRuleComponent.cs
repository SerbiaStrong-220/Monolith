using Content.Server._Exodus.StationEvents;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;

namespace Content.Server._Exodus.StationEvents.Components;

/// <summary>
/// Spawns an entity at a random position within a distance ring around the primary map origin.
/// </summary>
[RegisterComponent, Access(typeof(RingEntitySpawnRuleSystem))]
public sealed partial class RingEntitySpawnRuleComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Prototype = string.Empty;

    [DataField]
    public float MinimumDistance;

    [DataField]
    public float MaximumDistance;

    /// <summary>
    /// Localized announcement shown after a successful spawn. Receives <c>x</c> and <c>y</c> arguments.
    /// </summary>
    [DataField]
    public LocId? Announcement;

    [DataField]
    public SoundSpecifier? AnnouncementSound;

    [DataField]
    public Color AnnouncementColor = Color.Gold;
}
