namespace Content.Shared._Exodus.NPC.Pet;

[RegisterComponent]
public sealed partial class PetOwnerMindComponent : Component
{
    [ViewVariables]
    public HashSet<EntityUid> Pets = new();
}
