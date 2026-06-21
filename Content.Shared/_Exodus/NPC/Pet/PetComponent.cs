namespace Content.Shared._Exodus.NPC.Pet;

[RegisterComponent]
public sealed partial class PetComponent : Component
{
    /// <summary>The master's current body. Named "Master" to avoid conflict with Component.Owner, lol.
    /// Reassigned when master was cloned.</summary>
    [ViewVariables]
    public EntityUid? Master;

    /// <summary>Master's mind entity — something that survives cloning/body swaps.</summary>
    [ViewVariables]
    public EntityUid? MasterMind;
}
