using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.Asakim;

[RegisterComponent, Access(typeof(AsakimBrainPinpointerSystem)), AutoGenerateComponentPause]
public sealed partial class AsakimBrainPinpointerComponent : Component
{
    /// <summary>
    /// How often the pinpointer rescans for the nearest Asakim brain while active.
    /// Also acts as a cooldown on manual toggle-triggered searches to prevent spam.
    /// </summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(5);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;
}
