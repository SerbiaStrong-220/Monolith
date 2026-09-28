namespace Content.Shared._Exodus.Virology.Intelligent;

[RegisterComponent]
public sealed partial class RotOrganVisualsComponent : Component
{
    [DataField] public int Layer;
    [DataField] public string ActiveState = "alive";
    [DataField] public string DormantState = "dormant";
    [DataField] public string? GrowthPrefix;
    [DataField] public bool Tissue;
    [DataField] public bool Nutrition;
    [DataField] public string FullState = "full";
    [DataField] public string PartialState = "partial";
    [DataField] public bool Enabled = true;
    [DataField] public string GrowingState = "growing";
    [DataField] public TimeSpan GrowDuration = TimeSpan.FromSeconds(0.8);
}
