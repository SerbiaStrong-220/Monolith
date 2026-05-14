using System;

namespace Content.Server._Exodus.Implants;

[RegisterComponent, Access(typeof(DelayedDeathAcidifierSystem))]
public sealed partial class DelayedDeathAcidifierComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(10);

    [ViewVariables]
    public TimeSpan? TriggerAt;

    [ViewVariables]
    public bool Triggered;
}
