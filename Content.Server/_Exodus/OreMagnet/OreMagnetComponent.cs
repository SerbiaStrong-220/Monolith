using System.Diagnostics.CodeAnalysis;
using Content.Shared.Whitelist;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Server._Exodus.OreMagnet;

[RegisterComponent]
public sealed partial class OreMagnetComponent : Component
{
    /// <summary>
    /// Search radius in tiles.
    /// </summary>
    [DataField]
    public float Radius = 8f;

    /// <summary>
    /// Speed at which attracted entities are thrown toward the magnet.
    /// </summary>
    [DataField]
    public float PullSpeed = 6f;

    /// <summary>
    /// How long the magnet stays active after a signal, in seconds.
    /// </summary>
    [DataField]
    public float ActivationDuration = 10f;

    /// <summary>
    /// Whitelist of entity types to attract. If null, attracts everything in range.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// DeviceLink sink port that triggers activation.
    /// </summary>
    [DataField]
    public string OnPort = "On";

    /// <summary>
    /// Absolute server time at which the magnet deactivates.
    /// Null when the magnet is inactive. Avoids per-tick float subtraction drift.
    /// </summary>
    [ViewVariables]
    public TimeSpan? DeactivateAt;

    [ViewVariables]
    public TimeSpan? LidCloseAt;

    [MemberNotNullWhen(true, nameof(DeactivateAt))]
    public bool IsActive => DeactivateAt.HasValue;
}
