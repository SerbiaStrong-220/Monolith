using Content.Shared.Explosion;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Configures lightning strikes this nebula inflicts on grids and players inside it.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaLightningHazardComponent : Component
{
    [DataField]
    public TimeSpan SmallStrikeInterval = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan HeavyStrikeInterval = TimeSpan.FromSeconds(30);

    [DataField]
    public float SmallShieldLoad = 450f;

    [DataField]
    public float HeavyShieldLoad = 2000f;

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

    [DataField]
    public float PlayerRange = 32f;
}
