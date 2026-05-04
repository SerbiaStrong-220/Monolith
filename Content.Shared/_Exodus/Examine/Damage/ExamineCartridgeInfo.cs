// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Provstat
using Content.Shared.Damage;

namespace Content.Shared._Exodus.Examine.Damage;

public readonly struct ExamineCartridgeInfo
{
    public DamageSpecifier? Damage { get; init; }

    public float? ArmorPenetration { get; init; }

    public ExamineExplosionInfo? Explosion { get; init; }
    public ExamineEmpInfo? Emp { get; init; }
}
