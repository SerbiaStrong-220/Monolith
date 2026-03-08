// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: DarkBanOne
namespace Content.Shared._Exodus.Stealth.Components;

[RegisterComponent]
public sealed partial class StealthOnHeldComponent : Component
{
    [DataField("stealth")]
    public StealthData Stealth = new();
}
