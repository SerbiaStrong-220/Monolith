namespace Content.Client._Exodus.Stances;

[RegisterComponent]
public sealed partial class LocomotionStanceVisualsComponent : Component
{
    [DataField]
    public string Layer = "body";

    public string? CurrentState;
}
