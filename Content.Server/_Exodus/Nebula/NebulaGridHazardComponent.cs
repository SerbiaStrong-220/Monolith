using Content.Shared.Explosion;
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
    public TimeSpan GreenEmpInterval = TimeSpan.FromSeconds(30);

    [DataField]
    public float PlayerRange = 32f;

    [DataField]
    public float SmallShieldLoad = 450f;

    [DataField]
    public float HeavyShieldLoad = 2000f;

    [DataField]
    public float GreenEmpRange = 8f;

    [DataField]
    public float GreenEmpEnergyConsumption = 500000f;

    [DataField]
    public float GreenEmpDisableDuration = 5f;

    [DataField]
    public ProtoId<ExplosionPrototype> SmallExplosionType = "Minibomb";

    [DataField]
    public float SmallExplosionTotalIntensity = 133.333f;

    [DataField]
    public float SmallExplosionIntensitySlope = 30f;

    [DataField]
    public float SmallExplosionMaxTileIntensity = 40f;

    [DataField]
    public ProtoId<ExplosionPrototype> HeavyExplosionType = "Minibomb";

    [DataField]
    public float HeavyExplosionTotalIntensity = 1066.667f;

    [DataField]
    public float HeavyExplosionIntensitySlope = 30f;

    [DataField]
    public float HeavyExplosionMaxTileIntensity = 80f;

    // Temporary visual stretch until dedicated heavy lightning sprites are added.
    [DataField]
    public float SmallLightningLength = 8f;

    [DataField]
    public float HeavyLightningLength = 16f;

    [DataField]
    public EntProtoId SmallLightningPrototype = "NebulaRedSmallStrikeVisual";

    [DataField]
    public EntProtoId HeavyLightningPrototype = "NebulaRedHeavyStrikeVisual";

    [DataField]
    public SoundSpecifier SmallImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/small_lighting_impact.ogg");

    [DataField]
    public SoundSpecifier HeavyImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/medium_lighting_impact.ogg");

    [DataField]
    public SoundSpecifier ShieldImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/shield_lighting_impact.ogg");

    [ViewVariables]
    public bool TimersInitialized;

    [ViewVariables]
    public TimeSpan NextSmallStrike;

    [ViewVariables]
    public TimeSpan NextHeavyStrike;

    [ViewVariables]
    public TimeSpan NextGreenEmp;

    [ViewVariables]
    public TimeSpan LastSmallStrike;

    [ViewVariables]
    public TimeSpan LastHeavyStrike;

    [ViewVariables]
    public TimeSpan LastGreenEmp;

    [ViewVariables]
    public TimeSpan LastSmallDelta;

    [ViewVariables]
    public TimeSpan LastHeavyDelta;

    [ViewVariables]
    public TimeSpan LastGreenEmpDelta;

    [ViewVariables]
    public int SmallStrikeCount;

    [ViewVariables]
    public int HeavyStrikeCount;

    [ViewVariables]
    public int GreenEmpCount;
}
