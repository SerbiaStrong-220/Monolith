using System;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.Implants;

[RegisterComponent, Access(typeof(DelayedDeathAcidifierSystem)), AutoGenerateComponentPause]
public sealed partial class DelayedDeathAcidifierComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(10);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan? TriggerAt;

    [ViewVariables]
    public bool Triggered;
}
