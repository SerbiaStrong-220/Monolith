using Robust.Shared.GameObjects;

namespace Content.Shared._Exodus.Power.Components;

/// <summary>
/// When added to a machine with ApcPowerReceiver and RadiationSource,
/// enables radiation only while the machine is powered and active.
/// Works with any machine - not specific to thrusters.
/// </summary>
[RegisterComponent]
public sealed partial class PoweredRadiationSourceComponent : Component
{
    /// <summary>
    /// Whether the owning machine is currently doing work that should emit radiation.
    /// </summary>
    [DataField]
    public bool Active;
}
