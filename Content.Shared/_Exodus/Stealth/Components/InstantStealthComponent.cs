// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: DarkBanOne
using Content.Shared._Exodus.Stealth;

namespace Content.Shared._Exodus.Stealth.Components;

[RegisterComponent]
public sealed partial class InstantStealthComponent : Component
{
    [DataField("stealth")]
    public StealthData Stealth = new();

    [DataField("enabled")]
    public bool Enabled = true;
}
