using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.NPC.Pet;

[RegisterComponent]
public sealed partial class PetComponent : Component
{
    /// <summary>The master's current body. Named "Master" to avoid conflict with Component.Owner, lol.
    [ViewVariables]
    public EntityUid? Master;

    /// <summary>Master's mind entity — survives cloning/body swaps.</summary>
    [ViewVariables]
    public EntityUid? MasterMind;

    /// <summary>Faction pet joins when it goes feral.</summary>
    [DataField]
    public ProtoId<NpcFactionPrototype> FeralFaction = "SimpleHostile";

    /// <summary>HTN compound pet switches to when it goes feral.</summary>
    [DataField]
    public string FeralCompound = "SimpleHostileCompound";
}
