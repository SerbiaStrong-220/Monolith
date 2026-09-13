namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>A finite food supply on the original body; detached limbs do not duplicate it.</summary>
[RegisterComponent]
public sealed partial class RotCorpseNutritionComponent : Component
{
    [DataField]
    public int Remaining;
}
