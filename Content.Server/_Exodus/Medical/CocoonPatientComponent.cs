namespace Content.Server._Exodus.Medical;

/// <summary>Treatment owned by a cocoon; never grants permanent respiratory immunity.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class CocoonPatientComponent : Component
{
    /// <summary>The shelter currently treating this patient.</summary>
    [DataField] public EntityUid Cocoon;
    /// <summary>Time of the next treatment pulse.</summary>
    [DataField, AutoPausedField] public TimeSpan NextHeal;
}
