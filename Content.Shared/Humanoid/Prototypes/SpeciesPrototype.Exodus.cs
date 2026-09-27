// Exodus: species-specific genetic capacity, independent of copied appearances.
namespace Content.Shared.Humanoid.Prototypes;

public sealed partial class SpeciesPrototype
{
    /// <summary>Maximum genetic load before instability damages the body.</summary>
    [DataField]
    public int GeneticStabilityCapacity = 60;
}
