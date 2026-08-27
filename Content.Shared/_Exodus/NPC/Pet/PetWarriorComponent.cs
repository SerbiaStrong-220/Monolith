namespace Content.Shared._Exodus.NPC.Pet;

[RegisterComponent]
public sealed partial class PetWarriorComponent : Component
{
    /// <summary>How long pet keeps attacking triggered target.</summary>
    [DataField]
    public TimeSpan AggroMemory = TimeSpan.FromSeconds(15);

    /// <summary>If true, the master cannot damage this pet.</summary>
    [DataField]
    public bool ImmuneToMaster = true;

    /// <summary>Active triggered targets -> time they are forgotten.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> AggroMemories = new();
}
