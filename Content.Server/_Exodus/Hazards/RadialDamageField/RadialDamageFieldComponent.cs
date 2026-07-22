// (c) Space Exodus Team - EXDS-RL with CLA

using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Hazards.RadialDamageField;

/// <summary>
/// Periodically damages matching <see cref="RadialDamageFieldTargetComponent"/> entities within range.
/// </summary>
[RegisterComponent]
public sealed partial class RadialDamageFieldComponent : Component
{
    /// <summary>
    /// Configuration used by this field emitter.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<RadialDamageFieldProfilePrototype> Profile;

    /// <summary>
    /// Optional per-emitter range. If unset, the profile range is used.
    /// </summary>
    [DataField]
    public float? RangeOverride;
}
