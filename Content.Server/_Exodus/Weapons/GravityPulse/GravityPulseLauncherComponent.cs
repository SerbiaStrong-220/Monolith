namespace Content.Server._Exodus.Weapons.GravityPulse;

/// <summary>
/// Controls whether the projectiles in one shot share their hit history.
/// </summary>
[RegisterComponent]
public sealed partial class GravityPulseLauncherComponent : Component
{
    /// <summary>When enabled, overlapping projectiles from one shot can hit each target only once.</summary>
    [DataField]
    public bool ShareHits = true;
}
