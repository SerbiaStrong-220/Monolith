using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Shipyard;

/// <summary>
/// Identifies the ship, price and snapshot version shown before confirming a paid replacement.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct RepairSnapshotQuote(NetEntity Grid, int Price, int Revision);

[Serializable, NetSerializable]
public sealed class ShipyardRepairSnapshotMessage(RepairSnapshotQuote quote) : BoundUserInterfaceMessage
{
    public readonly RepairSnapshotQuote Quote = quote;
}
