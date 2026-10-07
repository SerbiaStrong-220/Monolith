using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// Increases active shield regeneration after a configurable interval without impacts.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ShipShieldRecoveryBoostComponent : Component
{
    /// <summary>
    /// Time without shield impacts required to enable accelerated regeneration.
    /// </summary>
    [DataField]
    public TimeSpan RecoveryDelay = TimeSpan.FromSeconds(9);

    /// <summary>
    /// Multiplier applied to active regeneration once the recovery delay has elapsed.
    /// </summary>
    [DataField]
    public float Multiplier = 1.4f;

    /// <summary>
    /// Earliest time at which regeneration can be accelerated, preserved across map pauses.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan BoostReadyAt;
}
