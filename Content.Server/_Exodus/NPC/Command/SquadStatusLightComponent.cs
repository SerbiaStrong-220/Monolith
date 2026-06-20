namespace Content.Server._Exodus.NPC.Command;

// =====================================================================================================
// TEST-ONLY / DEBUG. Tints a smart mob's PointLight to show its squad status while we diagnose the
// command system. Remove this component, SquadStatusLightSystem, and the PointLight/SquadStatusLight
// entries on the Smart mobs before release.
// =====================================================================================================
/// <summary>
/// Drives a mob's PointLight colour from its current squad status (see <see cref="SquadStatusLightSystem"/>):
/// yellow = no commander, green = in a squad (Follow), red = Attack, blue = Retreat, orange = Hold,
/// purple = anything else.
/// </summary>
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
