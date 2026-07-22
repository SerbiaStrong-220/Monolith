using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Hazards;

/// <summary>
/// Configures personal lightning strikes the nebula inflicts on free-space (EVA) entities
/// inside it. Sits on a nebula marker prototype; the lightning system reads it on each
/// strike via the entity's <see cref="NebulaPresenceComponent.Marker"/>.
/// Decoupled from <see cref="NebulaLightningHazardComponent"/> so grid hazards and space
/// hazards can be enabled independently per marker.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaSpaceLightningHazardComponent : Component
{
    [DataField]
    public int MinStrikeDelaySeconds = 5;

    [DataField]
    public int MaxStrikeDelaySeconds = 20;

    [DataField]
    public int ShockDamage = 15;

    [DataField]
    public DamageSpecifier BurnDamage = new()
    {
        DamageDict =
        {
            { "Heat", 15 },
        },
    };

    [DataField]
    public TimeSpan ShockTime = TimeSpan.FromSeconds(2);

    [DataField]
    public EntProtoId LightningPrototype = "NebulaRedSmallStrikeVisual";

    [DataField]
    public float LightningLength = 8f;

    [DataField]
    public SoundSpecifier ImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/small_lightning_impact.ogg");

    [DataField]
    public SoundSpecifier ShieldImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/shield_lightning_impact.ogg");

    [DataField]
    public float ShieldLoad = 200f;

    [DataField]
    public float ImpactSoundRange = 96f;

    [DataField]
    public float ImpactSoundVolume = -4f;
}
