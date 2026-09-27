using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Weapons.Hardpoints;

/// <summary>
/// Mechanical mount compatibility, independent of the gunnery console's weapon categories.
/// </summary>
[Serializable, NetSerializable]
public enum ExodusHardpointClass : byte
{
    Ballistic,
    Energy,
    Missile,
    Universal,
}
