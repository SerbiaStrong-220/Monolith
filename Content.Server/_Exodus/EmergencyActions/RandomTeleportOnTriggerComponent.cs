using Content.Shared.Teleportation;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.EmergencyActions;

/// <summary>
/// Randomly teleports the trigger user.
/// </summary>
[RegisterComponent, Access(typeof(RandomTeleportOnTriggerSystem)), AutoGenerateComponentPause]
public sealed partial class RandomTeleportOnTriggerComponent : Component
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
