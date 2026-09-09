namespace Content.Shared._Exodus.NPC.Pet;

[ByRefEvent]
public readonly record struct PetOwnerChangedEvent(EntityUid? Master);
