using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.AutoMining;

/// <summary>Radar beam target relative to its grid, independent of the emitter's PVS visibility.</summary>
/// <param name="LinkPartner">Partner laser of a consortium link; the target is then that laser's pivot.</param>
[Serializable, NetSerializable]
public readonly record struct BulkAutoMiningRadarBeam(NetCoordinates Target, float MuzzleOffset, NetEntity? LinkPartner = null);
