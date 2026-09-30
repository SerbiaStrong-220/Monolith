using Content.Shared.FixedPoint;
using Robust.Shared.Audio;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilityStateComponent
{
    /// <summary>Whether the current genome contributes periodic blood expulsion.</summary>
    [DataField] public bool BloodExpulsionEnabled;
    /// <summary>Time between involuntary bursts, including the delay after activation.</summary>
    [DataField] public TimeSpan BloodExpulsionInterval = TimeSpan.FromSeconds(30);
    /// <summary>Scheduled burst time, shifted when the carrier is paused.</summary>
    [DataField, AutoPausedField] public TimeSpan NextBloodExpulsion;
    /// <summary>Fraction of the body's full blood capacity removed by each burst.</summary>
    [DataField] public float BloodExpulsionFraction = 0.1f;
    /// <summary>Bleeding rate added by each burst, before physiological modifiers.</summary>
    [DataField] public float BloodExpulsionBleedAmount = 1f;
    /// <summary>Maximum distance, in meters, to tiles covered by the burst.</summary>
    [DataField] public float BloodExpulsionRadius = 2.5f;
    /// <summary>Extra blood produced on each reachable tile; reduced proportionally if the carrier is nearly empty.</summary>
    [DataField] public FixedPoint2 BloodExpulsionSpillAmount = 10;
    /// <summary>One positional sound per burst, instead of one sound per puddle.</summary>
    [DataField] public SoundSpecifier BloodExpulsionSound = new SoundPathSpecifier("/Audio/_Exodus/Genetics/blood_bath.ogg")
    {
        Params = AudioParams.Default.WithVolume(-2f).WithMaxDistance(12f),
    };
}
