using Robust.Shared.GameObjects;

namespace Content.Shared._Exodus.Power.Components;

/// <summary>
/// When added to a machine with ApcPowerReceiver and RadiationSource,
/// enables radiation only while the machine is powered.
/// Works with any machine — not specific to thrusters.
/// </summary>
[RegisterComponent]
public sealed partial class PoweredRadiationSourceComponent : Component;
