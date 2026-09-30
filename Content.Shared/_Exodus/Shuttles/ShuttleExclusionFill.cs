using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Shuttles;

/// <summary>
/// How a no-FTL zone is painted on the shuttle console FTL map.
/// </summary>
[Serializable, NetSerializable]
public enum ShuttleExclusionFill : byte
{
    /// <summary>
    /// Faint solid tint.
    /// </summary>
    Solid,

    /// <summary>
    /// Diagonal hatching.
    /// </summary>
    Hatched,
}
