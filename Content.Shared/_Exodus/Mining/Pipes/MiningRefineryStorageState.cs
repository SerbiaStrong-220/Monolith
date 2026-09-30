using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.Pipes;

[Serializable, NetSerializable]
/// <param name="LinkedShips">Ships whose liquid metal networks are joined with this refinery's network.</param>
/// <param name="LinkBonus">Consortium speed and liquid metal yield bonus applied to this refinery, as a fraction.</param>
public readonly record struct MiningRefineryStorageState(
    float GasMoles,
    float GasPressure,
    int SlurryStored,
    int? SlurryCapacity,
    int LinkedShips = 1,
    float LinkBonus = 0);
