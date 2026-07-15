// (c) Space Exodus Team
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Exodus.ShipArmor;

/// <summary>
/// Passive local ship armor block. Absorbs damage to entities on the same grid within
/// <see cref="Radius"/> and regenerates charge over time without power.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class ShipArmorComponent : Component
{
    /// <summary>
    /// Maximum absorption pool.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 MaxCharge = 500;

    /// <summary>
    /// Remaining absorption pool. Filled to <see cref="MaxCharge"/> on MapInit when left at zero.
    /// </summary>
    [DataField, AutoNetworkedField]
    public FixedPoint2 CurrentCharge;

    /// <summary>
    /// World-unit protection radius around this block (1 tile ≈ 1 unit).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Radius = 4f;

    /// <summary>
    /// Charge restored per second while regenerating.
    /// </summary>
    [DataField]
    public FixedPoint2 RegenRate = 25;

    /// <summary>
    /// Delay after absorbing damage before regeneration starts.
    /// </summary>
    [DataField]
    public TimeSpan RegenDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often regeneration ticks. Larger values cut update/dirty cost.
    /// </summary>
    [DataField]
    public TimeSpan RegenInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Next regeneration tick. Pause-aware.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    [AutoPausedField]
    public TimeSpan NextUpdate;

    /// <summary>
    /// Charge spent per unit of absorbed damage.
    /// </summary>
    [DataField]
    public float ChargeCostMultiplier = 1f;

    /// <summary>
    /// Per damage-type absorption fraction (0–1). Empty = absorb all positive types at 100%.
    /// Missing keys in a non-empty dictionary are not absorbed.
    /// </summary>
    [DataField]
    public Dictionary<string, float> AbsorbRatios = new();

    /// <summary>
    /// Manual enable flag. Still requires anchoring on a grid to function.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    /// <summary>
    /// Whether examine shows charge / radius.
    /// </summary>
    [DataField]
    public bool ShowExamine = true;
}
