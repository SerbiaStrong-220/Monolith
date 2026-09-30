using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Genetics;

/// <summary>Weaving parameters for a bearer of the cocoon gene.</summary>
[RegisterComponent]
public sealed partial class GeneticCocoonOwnerComponent : Component
{
    /// <summary>Structure woven on successful completion.</summary>
    [DataField] public EntProtoId Prototype = "GeneticHealingCocoon";
    /// <summary>Uninterrupted time required to weave a cocoon.</summary>
    [DataField] public TimeSpan WeaveTime = TimeSpan.FromSeconds(10);
    /// <summary>Nutrition consumed only after successful weaving.</summary>
    [DataField] public float NutritionCost = 60;
    /// <summary>Hydration consumed only after successful weaving.</summary>
    [DataField] public float HydrationCost = 60;
}
