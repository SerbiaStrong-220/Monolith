// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared._NF.Market;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Server-wide resale stock for the current round, independent of station lifetime.
/// This runtime inventory is neither serialized to maps nor persisted in the database.
/// </summary>
[RegisterComponent]
[Access(typeof(MarketInventorySystem))]
public sealed partial class MarketInventoryComponent : Component
{
    public readonly Dictionary<EntProtoId, MarketData> Stock = new();

    /// <summary>Coalesces inventory mutations before notifying open terminals.</summary>
    public bool Changed;

    /// <summary>Earliest time another inventory notification may be sent.</summary>
    public TimeSpan NextNotification;
}
