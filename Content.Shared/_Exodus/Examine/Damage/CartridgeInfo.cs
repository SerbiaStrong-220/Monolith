// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Provstat
using Content.Shared.Damage;

namespace Content.Shared._Exodus.Examine.Damage;

public readonly struct CartridgeInfo
{
    public DamageSpecifier? Damage { get; init; }

    public float? ArmorPenetration { get; init; }
}
