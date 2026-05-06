namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Multiplies the thrust of any shuttle currently within this nebula.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaThrustReductionComponent : Component
{
    [DataField]
    public float Multiplier = 0.5f;
}
