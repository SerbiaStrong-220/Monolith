using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula;

[RegisterComponent]
public sealed partial class NebulaGridHazardComponent : Component
{
    [DataField]
    public TimeSpan SmallStrikeInterval = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan HeavyStrikeInterval = TimeSpan.FromSeconds(30);

    [DataField]
    public float PlayerRange = 32f;

    [DataField]
    public float SmallShieldLoad = 50f;

    [DataField]
    public float HeavyShieldLoad = 200f;

    [DataField]
    public int SmallTileRadius;

    [DataField]
    public int HeavyTileRadius = 1;

    [DataField]
    public DamageSpecifier SmallDamage = new()
    {
        DamageDict =
        {
            ["Shock"] = 20,
            ["Heat"] = 10,
            ["Structural"] = 50,
        },
    };

    [DataField]
    public DamageSpecifier HeavyDamage = new()
    {
        DamageDict =
        {
            ["Shock"] = 60,
            ["Heat"] = 30,
            ["Structural"] = 250,
        },
    };

    [DataField]
    public EntProtoId SmallLightningPrototype = "NebulaRedSmallLightning";

    [DataField]
    public EntProtoId HeavyLightningPrototype = "NebulaRedHeavyLightning";

    [DataField]
    public SoundSpecifier ShieldImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/shield_lighting_impact.mp3");

    [ViewVariables]
    public bool TimersInitialized;

    [ViewVariables]
    public TimeSpan NextSmallStrike;

    [ViewVariables]
    public TimeSpan NextHeavyStrike;
}
