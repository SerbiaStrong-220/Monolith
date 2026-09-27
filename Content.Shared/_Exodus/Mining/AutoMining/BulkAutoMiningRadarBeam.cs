using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.AutoMining;

/// <summary>Radar beam target relative to its grid, independent of the emitter's PVS visibility.</summary>
[Serializable, NetSerializable]
public readonly record struct BulkAutoMiningRadarBeam(NetCoordinates Target, float MuzzleOffset);
