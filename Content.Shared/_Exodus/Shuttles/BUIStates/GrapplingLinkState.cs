using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.BUIStates;

/// <summary>
/// State of the link between the grappling gun and its target.
/// </summary>
[Serializable, NetSerializable]
public sealed class GrapplingLinkState
{
    public MapCoordinates GunPos;
    public MapCoordinates TargetPos;
}
