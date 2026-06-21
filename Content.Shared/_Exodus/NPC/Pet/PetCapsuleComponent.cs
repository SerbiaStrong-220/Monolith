using Content.Shared.Materials;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.NPC.Pet;

[RegisterComponent]
public sealed partial class PetCapsuleComponent : Component
{
    /// <summary>Pet entity prototype.</summary>
    [DataField(required: true)]
    public EntProtoId Pet;

    /// <summary>Material this pet is built from, atm biomass or scrap.</summary>
    [DataField]
    public ProtoId<MaterialPrototype> Material = "Biomass";

    /// <summary>Material cost to reconstruct gibbed pet.</summary>
    [DataField]
    public int ReconstructCost = 50;

    /// <summary>Material cost to revive dead pet.</summary>
    [DataField]
    public int ReviveCost = 25;

    /// <summary>Sound played on deploy and recall, atm placeholder.</summary>
    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/Effects/teleport_arrival.ogg");

    /// <summary>The bound master's mind.</summary>
    [ViewVariables]
    public EntityUid? MasterMind;

    /// <summary>The bound pet state (summoned or stored). Null/deleted means gibbed.</summary>
    [ViewVariables]
    public EntityUid? PetEntity;

    /// <summary>True while pet hidden on storage map.</summary>
    [ViewVariables]
    public bool Stored;
}
