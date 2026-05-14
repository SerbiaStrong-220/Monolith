namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// When attached to a ship shield emitter, queues an explosion the moment its accumulated damage
/// crosses the configured DamageLimit (the rising edge that triggers overload).
/// Does NOT fire on power-loss recharge — only on damage-induced overload.
/// </summary>
[RegisterComponent, Access(typeof(ExplodeOnShieldOverloadSystem))]
public sealed partial class ExplodeOnShieldOverloadComponent : Component
{
    [DataField]
    public string ExplosionType = "HardBomb";

    [DataField]
    public float TotalIntensity = 4000f;

    [DataField]
    public float IntensitySlope = 3f;

    [DataField]
    public float MaxTileIntensity = 400f;

    /// <summary>
    /// Set to true once the explosion has fired so it never re-triggers for the same emitter.
    /// </summary>
    [ViewVariables]
    public bool Triggered;

    /// <summary>
    /// Tracked across ticks to detect the rising edge Damage &lt;= Limit -&gt; Damage &gt; Limit.
    /// </summary>
    [ViewVariables]
    public bool WasOverLimit;
}
