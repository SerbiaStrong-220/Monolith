namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// When attached to a ship shield emitter, queues an explosion the moment the emitter is forced
/// into overload by damage — either by the power-draw cap (LoadDamage &gt;= MaxDraw) or by the
/// hard damage cap (Damage &gt; DamageLimit). Does NOT fire on plain power-loss recharge.
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
    /// Tracked across ticks to detect the rising edge of "overloaded by damage, while powered".
    /// </summary>
    [ViewVariables]
    public bool WasOverloadedByDamage;
}
