using Content.Shared.Damage;

namespace Content.Shared._Exodus.Examine.Damage;

public readonly struct ExamineExplosionInfo
{
    public DamageSpecifier? Damage { get; init; }
    public float TotalIntensity { get; init; }
    public float IntensitySlope { get; init; }
    public float MaxIntensity { get; init; }
}
