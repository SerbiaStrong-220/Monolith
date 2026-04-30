using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula;

[RegisterComponent]
public sealed partial class NebulaSpaceLightningTargetComponent : Component
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
    public float LightningLength = 6f;

    [DataField]
    public SoundSpecifier ImpactSound = new SoundPathSpecifier("/Audio/_Exodus/Nebula/small_lighting_impact.ogg");

    [DataField]
    public float ImpactSoundRange = 96f;

    [DataField]
    public float ImpactSoundVolume = -4f;

    [ViewVariables]
    public TimeSpan NextStrike;

    [ViewVariables]
    public TimeSpan LastStrike;

    [ViewVariables]
    public int StrikeCount;
}
