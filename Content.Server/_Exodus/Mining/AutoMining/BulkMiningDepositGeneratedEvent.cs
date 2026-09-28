namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>
/// Raised on a resource grid after its original terrain is generated, before it can split.
/// Only grids explicitly configured with BulkMiningDeposit receive mining rights.
/// </summary>
[ByRefEvent]
public readonly record struct BulkMiningDepositGeneratedEvent;
