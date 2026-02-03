using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared.Exodus.SpaceArtillery.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipGrapplingGunComponent : Component
{
    [DataField, AutoNetworkedField]
    public string JointId = string.Empty;

    [DataField, AutoNetworkedField]
    public EntityUid? Projectile;

    [DataField, ViewVariables]
    public SpriteSpecifier RopeSprite =
        new SpriteSpecifier.Rsi(new ResPath("Objects/Weapons/Guns/Launchers/grappling_gun.rsi"), "rope");
}
