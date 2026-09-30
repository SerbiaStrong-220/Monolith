using Content.Shared._Exodus.Genetics;
using Content.Shared.Alert;
using Content.Shared.Body.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilityStateComponent
{
    /// <summary>Active adaptation and the unmodified underlying appearance, independent of mimicry.</summary>
    [DataField] public ProtoId<GeneticTransformationPrototype>? Transformation;
    [DataField] public HumanoidAppearanceComponent? GeneticOriginalAppearance;
    [DataField] public bool NativeTransformation;
    /// <summary>Stable coloration and cooldown survive disabling and re-enabling the gene.</summary>
    [DataField] public Color? TransformationColor;
    [DataField, AutoPausedField] public TimeSpan TransformationAvailable;
    /// <summary>Current humanoid appearance saved while in the alternate form; anatomy stays on this entity.</summary>
    [DataField] public HumanoidAppearanceComponent? FormAppearance;
    /// <summary>Only values actually changed by the adaptation are restored.</summary>
    public ProtoId<ReagentPrototype>? OriginalBloodReagent;
    public ProtoId<DamageModifierSetPrototype>? OriginalDamageModifier;
    public bool ChangedDamageModifier;
    public readonly Dictionary<EntityUid, HashSet<ProtoId<MetabolizerTypePrototype>>?> OriginalMetabolizers = new();
    public readonly Dictionary<EntityUid, ProtoId<AlertPrototype>> OriginalBreathingAlerts = new();
    public readonly Dictionary<string, (IComponent? Original, IComponent Applied)> OriginalPhysiology = new();
}
