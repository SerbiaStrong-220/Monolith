namespace Content.Server._Exodus.Virology.Intelligent;

/// <summary>Finite larva bites; consumed atomically when the feeding do-after completes.</summary>
[RegisterComponent]
public sealed partial class RotNutritionBlobComponent : Component
{
    [DataField] public int Remaining;
    [DataField] public int Initial;
}
