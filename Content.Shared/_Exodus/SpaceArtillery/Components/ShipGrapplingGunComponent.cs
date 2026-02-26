using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Utility;
using Robust.Shared.Physics.Dynamics.Joints;
using System.Numerics;

namespace Content.Shared.Exodus.SpaceArtillery.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipGrapplingGunComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? JointId = string.Empty;

    [DataField, AutoNetworkedField]
    public EntityUid? Projectile;

    [DataField, AutoNetworkedField]
    public EntityUid? TargetGrid;

    [DataField, AutoNetworkedField]
    public EntityUid? Target;

    [DataField, AutoNetworkedField]
    public Vector2 GunVisualOffset = new Vector2(0f, 0.5f);

    [DataField, ViewVariables]
    public SpriteSpecifier RopeSprite =
        new SpriteSpecifier.Rsi(new ResPath("Objects/Weapons/Guns/Launchers/grappling_gun.rsi"), "rope");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? BreakSound = new SoundPathSpecifier("/Audio/Items/snap.ogg");
}
