using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Nebula.Generation;

/// <summary>
/// Concentric sub-zone of the world-end death zone, used to dispatch different marker
/// prototypes to the inner and outer halves split by <see cref="WorldEndNebulaShape.MidRadius"/>.
/// </summary>
[Serializable, NetSerializable]
public enum WorldEndZone : byte
{
    Inner = 0,
    Outer = 1,
}
