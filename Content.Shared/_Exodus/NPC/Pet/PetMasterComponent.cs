namespace Content.Shared._Exodus.NPC.Pet;

[RegisterComponent]
public sealed partial class PetMasterComponent : Component
{
    [ViewVariables]
    public HashSet<EntityUid> Pets = new();
}
