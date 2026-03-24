using Robust.Shared.Map;
using Robust.Shared.Serialization;
using System.Numerics;
namespace Content.Shared._Exodus.BUIStates;

/// <summary>
/// State of the link between the grappling gun and its target.
/// </summary>
[Serializable, NetSerializable]
public sealed class GrapplingLinkState
{
    public Vector2 GunPos;
    public Vector2 TargetPos;

    public GrapplingLinkState(Vector2 gunPos, Vector2 targetPos)
    {
        GunPos = gunPos;
        TargetPos = targetPos;
    }
}
