namespace Content.Server._Exodus.Power.Components;

[RegisterComponent]
public sealed partial class PressurePowerGeneratorComponent : Component
{
    [DataField]
    public float MinimumPressure = 75f;

    [ViewVariables]
    public bool Running;
}
