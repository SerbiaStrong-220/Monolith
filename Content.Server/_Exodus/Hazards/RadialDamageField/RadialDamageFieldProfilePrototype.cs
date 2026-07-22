// (c) Space Exodus Team - EXDS-RL with CLA

using Content.Shared.Damage;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Hazards.RadialDamageField;

/// <summary>
/// Data-driven configuration shared by radial damage field emitters.
/// </summary>
[Prototype]
public sealed partial class RadialDamageFieldProfilePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public float Range { get; private set; } = 512f;

    [DataField(required: true)]
    public HashSet<string> TargetProfiles { get; private set; } = new();

    [DataField(required: true)]
    public DamageSpecifier Damage { get; private set; } = new();

    [DataField]
    public bool IgnoreResistances { get; private set; }

    [DataField]
    public LocId? TargetPopup { get; private set; }
}
