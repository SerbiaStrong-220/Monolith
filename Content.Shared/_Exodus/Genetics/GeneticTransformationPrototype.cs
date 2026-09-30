using Content.Shared.Alert;
using Content.Shared.Body.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Genetics;

/// <summary>A reversible species adaptation and alternate appearance of the same body. Never spawns anatomy.</summary>
[Prototype]
public sealed partial class GeneticTransformationPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    /// <summary>Species expressed by the gene and the appearance used by its alternate form.</summary>
    [DataField(required: true)] public ProtoId<SpeciesPrototype> Species;
    [DataField(required: true)] public ProtoId<SpeciesPrototype> FormSpecies;
    /// <summary>One color is chosen per carrier; native members of the species keep their appearance.</summary>
    [DataField(required: true)] public List<Color> Colors = new();
    /// <summary>Minimum interval between entering the alternate form. Returning never consumes a cooldown.</summary>
    [DataField] public TimeSpan Cooldown = TimeSpan.FromMinutes(2);
    /// <summary>Tint of the alternate form's base sprite.</summary>
    [DataField] public Color FormColor = Color.White;
    /// <summary>Use the carrier's humanoid skin color instead of FormColor, ignoring any mimicry disguise.</summary>
    [DataField] public bool FormMatchesSkinColor;
    [DataField] public float FormMovementMultiplier = 1f;
    [DataField] public DamageSpecifier FormDamage = new();
    /// <summary>Clear accumulated damage after critical health forces a return. Never restores missing anatomy or revives the dead.</summary>
    [DataField] public bool HealOnCriticalReturn;
    /// <summary>Adapt existing organs and blood without replacing them, restoring volume, or healing damage.</summary>
    [DataField(required: true)] public HashSet<ProtoId<MetabolizerTypePrototype>> MetabolizerTypes = new();
    [DataField(required: true)] public ProtoId<AlertPrototype> BreathingAlert;
    [DataField(required: true)] public ProtoId<ReagentPrototype> BloodReagent;
    [DataField(required: true)] public ProtoId<DamageModifierSetPrototype> DamageModifierSet;
    /// <summary>Non-anatomical species behavior. Must not contain body, organs, health, inventory or containers.</summary>
    [DataField(serverOnly: true)] public ComponentRegistry Components = new();
}
