using Content.Shared.Damage.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Adminbus.Tower7G;

/// <summary>Converts a natural melee damage type while covered by a 7G field.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class Tower7GDamageConversionComponent : Component
{
    /// <summary>Positive damage of this type is converted before target resistances.</summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<DamageTypePrototype> SourceType;

    /// <summary>Converted damage is added to any existing damage of this type.</summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<DamageTypePrototype> TargetType;

    /// <summary>Server-calculated coverage, replicated for melee prediction.</summary>
    [ViewVariables, AutoNetworkedField]
    public bool Active;
}
