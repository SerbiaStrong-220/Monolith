using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.AutoMining;

[Serializable, NetSerializable]
public enum BulkMiningLinkShipStatus : byte
{
    /// <summary>Has a console and a free laser in line of sight.</summary>
    Available,

    /// <summary>All of its lasers are mining, linked, full or unpowered.</summary>
    NoFreeLaser,

    /// <summary>No pair of free lasers can see each other.</summary>
    Obstructed,

    /// <summary>This ship's request waits for an answer.</summary>
    OutgoingRequest,

    /// <summary>The other ship asked this one to link.</summary>
    IncomingRequest,

    Linked,
}

/// <summary>Why a ship link was broken, reported to the consoles of both ships.</summary>
[Serializable, NetSerializable]
public enum BulkMiningLinkBreakReason : byte
{
    Manual,
    Obstructed,
    Range,
    Power,
    Lost,
}

/// <param name="RequestTimeLeft">Seconds until a pending request in either direction expires; zero otherwise.</param>
[Serializable, NetSerializable]
public readonly record struct BulkMiningLinkShipState(
    NetEntity Grid,
    string Name,
    float Distance,
    BulkMiningLinkShipStatus Status,
    int FreeLasers,
    float RequestTimeLeft);

/// <param name="Obstructed">The last line-of-sight check failed; the link breaks if the obstruction persists.</param>
[Serializable, NetSerializable]
public readonly record struct BulkMiningLinkState(
    NetEntity Grid,
    string Name,
    NetEntity Laser,
    string LaserName,
    string PartnerLaserName,
    float Distance,
    float Range,
    bool Obstructed);

[Serializable, NetSerializable]
public readonly record struct BulkMiningConsortiumMemberState(NetEntity Grid, string Name);

[Serializable, NetSerializable]
public sealed class BulkMiningLinkUiState
{
    public List<BulkMiningLinkShipState> Ships = new();
    public List<BulkMiningLinkState> Links = new();

    /// <summary>Ships of this consortium, including this one. A lone ship is a consortium of one.</summary>
    public List<BulkMiningConsortiumMemberState> Members = new();

    /// <summary>Current refinery speed and liquid metal yield bonus, as a fraction.</summary>
    public float Bonus;

    /// <summary>Bonus after one more ship joins, shown to motivate linking.</summary>
    public float NextBonus;

    public int FreeLasers;

}

[Serializable, NetSerializable]
public sealed class BulkAutoMiningLinkRequestMessage(NetEntity grid) : BoundUserInterfaceMessage
{
    public NetEntity Grid = grid;
}

[Serializable, NetSerializable]
public sealed class BulkAutoMiningLinkAcceptMessage(NetEntity grid) : BoundUserInterfaceMessage
{
    public NetEntity Grid = grid;
}

/// <summary>Declines an incoming request or withdraws an outgoing one.</summary>
[Serializable, NetSerializable]
public sealed class BulkAutoMiningLinkDeclineMessage(NetEntity grid) : BoundUserInterfaceMessage
{
    public NetEntity Grid = grid;
}

[Serializable, NetSerializable]
public sealed class BulkAutoMiningLinkBreakMessage(NetEntity grid) : BoundUserInterfaceMessage
{
    public NetEntity Grid = grid;
}
