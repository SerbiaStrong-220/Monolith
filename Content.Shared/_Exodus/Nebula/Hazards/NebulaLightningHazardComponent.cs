using Content.Shared.Explosion;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Hazards;

/// <summary>
/// Configures lightning strikes this nebula inflicts on grids and players inside it.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaLightningHazardComponent : Component
{
    /// <summary>Enables periodic small strikes on grids inside the nebula.</summary>
    [DataField]
    public bool EnableSmall = true;

    /// <summary>Enables periodic heavy strikes on grids inside the nebula.</summary>
    [DataField]
    public bool EnableHeavy = true;

    /// <summary>Enables periodic superheavy strikes on grids inside the nebula.</summary>
    [DataField]
    public bool EnableSuperHeavy = false;

    [DataField]
    public TimeSpan SmallStrikeInterval = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan HeavyStrikeInterval = TimeSpan.FromSeconds(30);

    [DataField]
    public TimeSpan SuperHeavyStrikeInterval = TimeSpan.FromSeconds(15);

    [DataField]
    public float SmallShieldLoad = 450f;

    [DataField]
    public float HeavyShieldLoad = 2000f;

    [DataField]
    public float SuperHeavyShieldLoad = 20000f;

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
    public ProtoId<ExplosionPrototype> SuperHeavyExplosionType = "Minibomb";

    [DataField]
    public float SuperHeavyExplosionTotalIntensity = 10667f;

    [DataField]
    public float SuperHeavyExplosionIntensitySlope = 30f;

    [DataField]
    public float SuperHeavyExplosionMaxTileIntensity = 200f;

    [DataField]
    public float SmallLightningLength = 8f;

    [DataField]
    public float HeavyLightningLength = 16f;

    [DataField]
    public float SuperHeavyLightningLength = 32f;

    [DataField]
    public EntProtoId SmallLightningPrototype = "NebulaRedSmallStrikeVisual";

    [DataField]
    public EntProtoId HeavyLightningPrototype = "NebulaRedHeavyStrikeVisual";

    [DataField]
    public EntProtoId SuperHeavyLightningPrototype = "NebulaRedHeavyStrikeVisual";

    [DataField]
    public SoundSpecifier SmallImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/small_lightning_impact.ogg");

    [DataField]
    public SoundSpecifier HeavyImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/medium_lightning_impact.ogg");

    [DataField]
    public SoundSpecifier SuperHeavyImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/medium_lightning_impact.ogg");

    [DataField]
    public SoundSpecifier ShieldImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/shield_lightning_impact.ogg");
}
