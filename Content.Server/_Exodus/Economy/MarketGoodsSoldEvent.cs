// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Economy;

/// <summary>
/// Goods irrevocably accepted by a sale endpoint, before deletion. This does not award cargo bounties.
/// Roots may overlap; intake adds stock and applies price impact for each physical entity at most once.
/// Producers must check DynamicMarketSystem.Ready before accepting goods or paying their reward.
/// OriginalShip limits a shipyard return to contents added after that ship's purchase.
/// </summary>
[ByRefEvent]
public readonly record struct MarketGoodsSoldEvent(
    IReadOnlyCollection<EntityUid> Sold,
    EntityUid Source,
    EntityUid? OriginalShip = null);
