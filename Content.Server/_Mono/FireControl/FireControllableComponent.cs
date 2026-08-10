// Copyright Rane (elijahrane@gmail.com) 2025
// All rights reserved. Relicensed under AGPL with permission

using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Mono.FireControl;

[RegisterComponent]
public sealed partial class FireControllableComponent : Component
{
    /// <summary>
    /// Reference to the controlling server, if any.
    /// </summary>
    [ViewVariables]
    public EntityUid? ControllingServer = null;

    // Exodus-begin fire-control cursor optimization
    /// <summary>
    /// Console responsible for the current short auto-fire request.
    /// </summary>
    [ViewVariables]
    public EntityUid? ActiveFiringConsole;

    /// <summary>
    /// User whose shots belong to the active firing console.
    /// </summary>
    [ViewVariables]
    public EntityUid? ActiveFiringUser;
    // Exodus-end

    /// <summary>
    /// When the weapon can next be fired
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextFire = TimeSpan.Zero;

    /// <summary>
    /// Cooldown between firing, in seconds
    /// </summary>
    [DataField]
    public float FireCooldown = 0.2f;

    /// <summary>
    /// Optional explicit gunnery server processing power cost.
    /// </summary>
    [DataField("processingPowerCost")]
    public int? ProcessingPowerCost;
}
