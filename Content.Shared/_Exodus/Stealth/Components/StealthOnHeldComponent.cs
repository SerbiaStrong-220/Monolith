namespace Content.Shared.Exodus.Stealth.Components;

[RegisterComponent]
public sealed partial class StealthOnHeldComponent : Component
{
    [DataField("stealth")]
    public StealthData Stealth = new();
}
