using Content.Shared._Exodus.Mining.AutoMining;

namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>
/// Pending requests and cached consortium size of a ship. Links themselves live on the paired lasers.
/// Transient: links, requests and the derived consortium size are not saved.
/// </summary>
[RegisterComponent, UnsavedComponent, Access(typeof(BulkAutoMiningSystem))]
public sealed partial class BulkMiningLinkGridComponent : Component
{
    /// <summary>Number of ships in this consortium, refreshed when links are created or broken.</summary>
    [ViewVariables]
    public int ConsortiumSize = 1;

    /// <summary>Requesting ship and when its request expires.</summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, TimeSpan> Incoming = new();

    /// <summary>Requested ship and when this ship's request expires.</summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, TimeSpan> Outgoing = new();

    [ViewVariables]
    public TimeSpan NextRequestTime;
}
