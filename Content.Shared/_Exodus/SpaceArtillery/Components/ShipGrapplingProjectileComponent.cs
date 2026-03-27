// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: DarkBanOne

using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared._Exodus.SpaceArtillery.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipGrapplingProjectileComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Gun;

    [DataField, AutoNetworkedField]
    public EntityCoordinates LocalGunShotPos;
}
