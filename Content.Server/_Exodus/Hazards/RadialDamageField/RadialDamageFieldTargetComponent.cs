// (c) Space Exodus Team - EXDS-RL with CLA

namespace Content.Server._Exodus.Hazards.RadialDamageField;

/// <summary>
/// Makes an entity eligible for radial damage fields with a matching profile.
/// </summary>
[RegisterComponent]
public sealed partial class RadialDamageFieldTargetComponent : Component
{
    [DataField(required: true)]
    public HashSet<string> Profiles = new();
}
