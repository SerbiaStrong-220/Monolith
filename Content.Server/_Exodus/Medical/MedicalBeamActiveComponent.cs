using Content.Shared._Exodus.Medical;
using Content.Shared.Damage;

namespace Content.Server._Exodus.Medical;

/// <summary>Runtime state present only while a medigun has a requested patient.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class MedicalBeamActiveComponent : Component
{
    [ViewVariables]
    public EntityUid User;

    [ViewVariables]
    public EntityUid Target;

    /// <summary>The mode that established this channel; automatic channels do not need input heartbeats.</summary>
    [ViewVariables]
    public MedicalBeamMode Mode;

    /// <summary>Entity playing the treatment loop. Cleared on every interruption.</summary>
    [ViewVariables]
    public EntityUid? AudioStream;

    [ViewVariables, AutoPausedField]
    public TimeSpan InputExpires;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextCheck;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextHeal;

    /// <summary>Reusable treatment buffer, rebuilt from current injuries for each pulse.</summary>
    public readonly DamageSpecifier Healing = new();
}
