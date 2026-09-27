using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilityStateComponent
{
    /// <summary>Separate reflection source, so the mutation never replaces innate or equipment reflection.</summary>
    [DataField] public EntProtoId DeflectorPrototype = "GeneticDeflector";
    [DataField] public EntityUid? Deflector;

    [DataField] public EntProtoId GlowPrototype = "GeneticGlow";
    [DataField] public EntityUid? Glow;

    [DataField] public TimeSpan HearingDuration = TimeSpan.FromSeconds(5);
    [DataField] public TimeSpan HearingCooldown = TimeSpan.FromSeconds(20);
    [DataField, AutoPausedField] public TimeSpan HearingUntil;
    [DataField, AutoPausedField] public TimeSpan HearingAvailable;

    /// <summary>Only webs produced by this ability count toward its limit.</summary>
    [DataField] public EntProtoId WebPrototype = "GeneticWeb";
    [DataField] public int WebLimit = 6;
    [DataField] public List<EntityUid> Webs = new();
    [DataField] public float WebNutrition = 5f;
    [DataField] public TimeSpan WebCooldown = TimeSpan.FromSeconds(12);
    [DataField, AutoPausedField] public TimeSpan WebAvailable;

    [DataField] public EntProtoId FlamePrototype = "ProjectileGeneticFlame";
    [DataField] public float FlameSpeed = 8f;
    [DataField] public float FlameRange = 5f;
    [DataField] public float FlameNutrition = 10f;
    [DataField] public SoundSpecifier FlameSound = new SoundPathSpecifier("/Audio/Magic/fireball.ogg");
    [DataField] public TimeSpan FlameCooldown = TimeSpan.FromSeconds(20);
    [DataField, AutoPausedField] public TimeSpan FlameAvailable;
}
