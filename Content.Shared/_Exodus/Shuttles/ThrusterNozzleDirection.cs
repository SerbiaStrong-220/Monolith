using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Shuttles;

/// <summary>
/// Local thrust direction, also used as the appearance key for its exhaust layer.
/// The nozzle faces the opposite direction. Values follow ShuttleComponent's cardinal indices.
/// </summary>
[Serializable, NetSerializable]
public enum ThrusterNozzleDirection : byte
{
    South,
    East,
    North,
    West,
}
