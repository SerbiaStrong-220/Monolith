namespace Content.Server._Exodus.ShipShields;

/// <summary>
/// Configures a ship shield as a set of independently collapsible field layers.
/// </summary>
[RegisterComponent]
public sealed partial class LayeredShipShieldComponent : Component
{
    /// <summary>
    /// Maximum number of field layers projected by the emitter.
    /// </summary>
    [DataField]
    public int LayerCount = 1;

    /// <summary>
    /// Thickness of each visual field layer in world units.
    /// </summary>
    [DataField]
    public float LayerThickness = 1.3f;

    /// <summary>
    /// Distance between visual field layers in world units.
    /// </summary>
    [DataField]
    public float LayerGap;

    /// <summary>
    /// Fraction of the emitter's damage limit retained after a field layer collapses.
    /// </summary>
    [DataField]
    public float CollapseDamageFraction = 0.55f;

    /// <summary>
    /// Damage fraction below which a collapsed field layer can begin recovering.
    /// </summary>
    [DataField]
    public float RecoveryDamageThreshold = 0.55f;

    /// <summary>
    /// Time without shield impacts required to restore one collapsed field layer.
    /// </summary>
    [DataField]
    public TimeSpan RecoveryInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Additional deflection load multiplier per collapsed field layer.
    /// </summary>
    [DataField]
    public float DeflectionDamageModifierStep = 0.2f;

    /// <summary>
    /// Runtime number of field layers that are still stable.
    /// </summary>
    [ViewVariables]
    public int ActiveLayerCount;

    /// <summary>
    /// Time spent in a safe recovery window for the next field layer.
    /// </summary>
    [ViewVariables]
    public TimeSpan RecoveryAccumulator;
}
