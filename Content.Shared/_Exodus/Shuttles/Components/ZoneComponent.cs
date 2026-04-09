// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: DarkBanOne

using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Shuttles.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class ZoneComponent : Component
{
    [DataField]
    public float Radius = 256;

    [DataField]
    public LocId Text = "zone-component-safe-zone";

    [DataField]
    public Color Color = Color.LightGreen;

    [DataField]
    public Color TextColor = Color.OrangeRed;

    [DataField]
    public bool ShouldShowText = true;
}
