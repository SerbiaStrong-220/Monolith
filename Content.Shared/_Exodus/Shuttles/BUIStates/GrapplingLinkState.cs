// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: DarkBanOne

using Robust.Shared.Serialization;
namespace Content.Shared._Exodus.BUIStates;

/// <summary>
/// State of the link between the grappling gun and its target.
/// </summary>
[Serializable, NetSerializable]
public sealed class GrapplingLinkState
{
    public NetEntity Gun;
    public NetEntity Target;

    public GrapplingLinkState(NetEntity gun, NetEntity target)
    {
        Gun = gun;
        Target = target;
    }
}
