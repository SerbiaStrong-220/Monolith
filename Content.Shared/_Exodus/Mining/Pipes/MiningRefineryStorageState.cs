using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Mining.Pipes;

[Serializable, NetSerializable]
/// <param name="LinkedShips">Ships whose liquid metal networks are joined with this refinery's network.</param>
/// <param name="LinkBonus">Consortium yield bonus applied to this refinery through cheaper recipes, as a fraction.</param>
public readonly record struct MiningRefineryStorageState(
    float GasMoles,
    float GasPressure,
    int SlurryStored,
    int? SlurryCapacity,
    int LinkedShips = 1,
    float LinkBonus = 0,
    float FullnessDiscount = 0,
    float FilterDiscount = 0,
    int ActiveFilters = 0,
    int InstalledFilters = 0,
    int FilterCapacity = 0,
    int LocalSlurryStored = 0,
    int? LocalSlurryCapacity = null);

/// <summary>Immutable appearance snapshot; its dictionary is replaced, never modified after publication.</summary>
[Serializable, NetSerializable]
public readonly record struct MiningRefineryFilterAppearance(Dictionary<string, MiningRefineryFilterState> States);

[Serializable, NetSerializable]
public enum MiningRefineryVisuals : byte
{
    Filters,
}

[Serializable, NetSerializable]
public enum MiningRefineryFilterState : byte
{
    Empty,
    Intact,
    Depleted,
}
