using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.Pipes;

[Serializable, NetSerializable]
public readonly record struct MiningRefineryStorageState(float GasMoles, float GasPressure, int SlurryStored, int? SlurryCapacity);
