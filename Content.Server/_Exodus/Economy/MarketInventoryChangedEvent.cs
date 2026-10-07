// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Economy;

/// <summary>Raised outside inventory mutations so interface refreshes cannot reenter a purchase.</summary>
[ByRefEvent]
public readonly record struct MarketInventoryChangedEvent;
