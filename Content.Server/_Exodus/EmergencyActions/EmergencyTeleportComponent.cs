using Content.Shared.Teleportation;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.EmergencyActions;

/// <summary>
/// Attach to a clothing item. When its wearer transitions from Alive to Critical, performs a
/// random teleport according to <see cref="Specifier"/>. Grants the wearer a passive action so
/// they can see the cooldown / status in their HUD. No identity / faction check is performed.
/// </summary>
[RegisterComponent, Access(typeof(EmergencyTeleportSystem)), AutoGenerateComponentPause]
public sealed partial class EmergencyTeleportComponent : Component
{
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextActivation = TimeSpan.Zero;

    [DataField]
    public TeleportSpecifier Specifier = new()
    {
        TeleportRadius = 40f,
        TeleportAttempts = 5,
        ForceSafe = false,
        MinRadiusFraction = 0.75f,
    };

    [DataField]
    public EntProtoId ActionProto = "ActionEmergencyTeleport";

    [DataField]
    public EntityUid? ActionUid;
}
