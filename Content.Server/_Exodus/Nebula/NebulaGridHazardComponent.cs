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
    public float PlayerRange = 32f;

    [DataField]
    public float SmallShieldLoad = 50f;

    [DataField]
    public float HeavyShieldLoad = 200f;

    [DataField]
    public ProtoId<ExplosionPrototype> SmallExplosionType = "Minibomb";

    [DataField]
    public float SmallExplosionTotalIntensity = 200f;

    [DataField]
    public float SmallExplosionIntensitySlope = 30f;

    [DataField]
    public float SmallExplosionMaxTileIntensity = 60f;

    [DataField]
    public ProtoId<ExplosionPrototype> HeavyExplosionType = "Minibomb";

    [DataField]
    public float HeavyExplosionTotalIntensity = 1600f;

    [DataField]
    public float HeavyExplosionIntensitySlope = 30f;

    [DataField]
    public float HeavyExplosionMaxTileIntensity = 120f;

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
    public SoundSpecifier SmallImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/small_lighting_impact.mp3");

    [DataField]
    public SoundSpecifier HeavyImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/medium_lighting_impact.mp3");

    [DataField]
    public SoundSpecifier ShieldImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/shield_lighting_impact.mp3");

    [ViewVariables]
    public bool TimersInitialized;

    [ViewVariables]
    public TimeSpan NextSmallStrike;

    [ViewVariables]
    public TimeSpan NextHeavyStrike;
}
