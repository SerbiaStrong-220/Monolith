using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.AutoMining;

[Serializable, NetSerializable]
public readonly record struct BulkAutoMiningTargetState(NetEntity Grid, string? Name);
