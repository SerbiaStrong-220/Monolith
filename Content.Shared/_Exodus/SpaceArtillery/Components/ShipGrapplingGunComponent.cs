// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: DarkBanOne

using Robust.Shared.Map;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Audio;
using Robust.Shared.Utility;
using Robust.Shared.Physics.Dynamics.Joints;
using System.Numerics;

namespace Content.Shared._Exodus.SpaceArtillery.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipGrapplingGunComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? JointId;

    [DataField, AutoNetworkedField]
    public EntityUid? Projectile;

    [DataField, AutoNetworkedField]
    public EntityUid? TargetGrid;

    [DataField, AutoNetworkedField]
    public EntityUid? Target;

    [DataField, AutoNetworkedField]
    public Vector2 GunVisualOffset = new Vector2(0f, -0.5f);

    [DataField, AutoNetworkedField]
    public float JointOffset = 10.0f;

    [DataField, AutoNetworkedField]
    public float Stiffness = 1f;

    [DataField, AutoNetworkedField]
    public float MaxLength = 500f;

    [DataField, ViewVariables]
    public SpriteSpecifier RopeSprite =
        new SpriteSpecifier.Rsi(new ResPath("Objects/Weapons/Guns/Launchers/grappling_gun.rsi"), "rope");

    [DataField, AutoNetworkedField]
    public SoundSpecifier? BreakSound = new SoundPathSpecifier("/Audio/Items/snap.ogg");
}
