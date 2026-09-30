using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.AutoMining;

[Serializable, NetSerializable]
public enum BulkAutoMiningLaserStatus : byte
{
    Ready,
    Mining,
    Offline,
    Busy,
    Full,
    Blocked,
    Searching,
    /// <summary>Aimed at a partner ship's laser as part of a mining consortium; cannot mine.</summary>
    Linked,
}

[Serializable, NetSerializable]
public readonly record struct BulkAutoMiningLaserState(
    NetEntity Emitter,
    string Name,
    float CurrentHp,
    float MaxHp,
    BulkAutoMiningLaserStatus Status,
    int Stored,
    int Capacity,
    string? ConsoleName = null,
    float Warmup = 0,
    float YieldBonus = 0,
    string? LinkedShip = null);
