using Content.Shared._Exodus.Mining.AutoMining;

namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>
/// Pending consortium requests of a ship. Links themselves live on the paired lasers,
/// so nothing here can outlive or contradict them. Transient: links and requests are not saved.
/// </summary>
[RegisterComponent, UnsavedComponent, Access(typeof(BulkAutoMiningSystem))]
public sealed partial class BulkMiningLinkGridComponent : Component
{
    /// <summary>Requesting ship and when its request expires.</summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, TimeSpan> Incoming = new();

    /// <summary>Requested ship and when this ship's request expires.</summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, TimeSpan> Outgoing = new();

    [ViewVariables]
    public TimeSpan NextRequestTime;
}
