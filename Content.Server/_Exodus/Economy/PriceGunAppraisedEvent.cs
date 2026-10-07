// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Economy;

/// <summary>
/// Raised on a price gun after a successful scan so consumers can reuse its displayed price.
/// </summary>
[ByRefEvent]
public readonly record struct PriceGunAppraisedEvent(EntityUid Target, double Price);
