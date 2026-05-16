using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.EmergencyActions;

/// <summary>
/// Attach to a clothing item. When its wearer transitions from Alive to Critical, a configured
/// pool of reagents is injected into their bloodstream. No identity / faction check is performed —
/// it is expected that the clothing prototype itself gates who can wear it.
/// </summary>
[RegisterComponent, Access(typeof(EmergencyReagentInjectorSystem)), AutoGenerateComponentPause]
public sealed partial class EmergencyReagentInjectorComponent : Component
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
