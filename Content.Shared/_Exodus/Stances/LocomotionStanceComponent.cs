using Content.Shared.Actions;
using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Exodus.Stances;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class LocomotionStanceComponent : Component
{
    [DataField, AutoNetworkedField]
    public LocomotionStance Stance = LocomotionStance.Quadruped;

    [DataField, AutoNetworkedField]
    public LocomotionStance PreviousStance = LocomotionStance.Quadruped;

    [DataField]
    public LocomotionStance UncurledStance = LocomotionStance.Quadruped;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan TransitionEnd;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan AttackAnimationEnd;

    [DataField]
    public TimeSpan TransitionDuration = TimeSpan.FromSeconds(0.8);

    [DataField]
    public TimeSpan AttackAnimationDuration = TimeSpan.FromSeconds(0.4);

    [DataField]
    public float QuadrupedSpeedMultiplier = 2f;

    [DataField(required: true)]
    public DamageSpecifier UprightDamage = new();

    [DataField(required: true)]
    public DamageSpecifier QuadrupedDamage = new();

    [DataField]
    public EntProtoId UprightAttackAnimation = "WeaponArcClaw";

    [DataField]
    public EntProtoId QuadrupedAttackAnimation = "WeaponArcBite";

    [DataField(required: true)]
    public EntProtoId ToggleStanceAction;

    [DataField(required: true)]
    public EntProtoId ToggleRestAction;

    [DataField]
    public EntityUid? StanceAction;

    [DataField]
    public EntityUid? RestAction;
}

[Serializable, NetSerializable]
public enum LocomotionStance : byte
{
    Quadruped,
    Upright,
    Curled,
}

public sealed partial class ToggleLocomotionStanceEvent : InstantActionEvent;
public sealed partial class ToggleCurledRestEvent : InstantActionEvent;
