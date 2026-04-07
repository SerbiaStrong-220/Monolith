// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Shared.Damage;

namespace Content.Server._Exodus.Adminbus.Tower7G;

[RegisterComponent]
public sealed partial class Tower7GComponent : Component
{
    [DataField]
    public float Range = 512;

    [DataField]
    public float MinDamage = 25f;

    [DataField]
    public float MaxDamage = 60f;

    [DataField]
    public DamageSpecifier BaseDamage = new()
    {
        DamageDict = new()
        {
            {"Heat", 1},
        },
    };

    [DataField]
    public LocId TargetPopup = "tower-7g-target-popup";
}
