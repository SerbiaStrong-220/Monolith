namespace Content.Server._Exodus.NPC.HomePoint;

[RegisterComponent, Access(typeof(HomePointSystem))]
public sealed partial class HomePointComponent : Component
{
    /// <summary>
    /// This records the NPC's spawn point into its HTN blackboard, so behaviour can return it to a fixed home/lair point
    /// Position is grid-anchored, will(should) work on moving grid.
    /// </summary>
    [DataField]
    public string Key = "HomeCoordinates";
}
