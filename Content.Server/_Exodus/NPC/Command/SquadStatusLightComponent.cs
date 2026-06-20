namespace Content.Server._Exodus.NPC.Command;

// TEST-ONLY
[RegisterComponent]
public sealed partial class SquadStatusLightComponent : Component
{
    [DataField]
    public Color NoCommander = Color.Yellow;

    [DataField]
    public Color InSquad = Color.Green;

    [DataField]
    public Color Attacking = Color.Red;

    [DataField]
    public Color Retreating = Color.Blue;

    [DataField]
    public Color Holding = Color.Orange;

    [DataField]
    public Color Other = Color.Purple;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.5);

    [ViewVariables]
    public TimeSpan NextUpdate;
}
