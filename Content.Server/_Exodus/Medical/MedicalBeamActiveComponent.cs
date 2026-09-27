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

    [ViewVariables, AutoPausedField]
    public TimeSpan InputExpires;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextCheck;

    [ViewVariables, AutoPausedField]
    public TimeSpan NextHeal;

    /// <summary>Reusable treatment buffer, rebuilt from current injuries for each pulse.</summary>
    public readonly DamageSpecifier Healing = new();
}
