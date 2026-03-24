// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: DarkBanOne

using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.SpaceArtillery.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipGrapplingGunTargetComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Gun;
}
