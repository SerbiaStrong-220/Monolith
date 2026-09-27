namespace Content.Server._Exodus.Medical;

/// <summary>Coordinates mediguns treating the same patient without multiplying the healing rate.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class MedicalBeamPatientComponent : Component
{
    [ViewVariables]
    public EntityUid? Gun;

    /// <summary>Persists across changes of medic so switching guns cannot accelerate treatment.</summary>
    [ViewVariables, AutoPausedField]
    public TimeSpan NextHeal;
}
