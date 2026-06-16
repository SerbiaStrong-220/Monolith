using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.EmergencyActions;

/// <summary>
/// Injects a configured reagent pool into the trigger user.
/// </summary>
[RegisterComponent, Access(typeof(AddReagentOnTriggerSystem)), AutoGenerateComponentPause]
public sealed partial class AddReagentOnTriggerComponent : Component
{
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextActivation = TimeSpan.Zero;

    [DataField]
    public List<ReagentQuantity> Reagents = new();

    [DataField]
    public SoundSpecifier InjectSound = new SoundPathSpecifier("/Audio/Items/hypospray.ogg");
}
