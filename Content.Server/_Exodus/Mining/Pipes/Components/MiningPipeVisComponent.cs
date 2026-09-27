namespace Content.Server._Exodus.Mining.Pipes.Components;

/// <summary>
/// Updates sprite mask when adjacent pipe nodes connect (same pattern as <see cref="Power.Components.CableVisComponent"/>).
/// </summary>
[RegisterComponent]
public sealed partial class MiningPipeVisComponent : Component
{
    [DataField(required: true)]
    public string Node = "pipe";
}
