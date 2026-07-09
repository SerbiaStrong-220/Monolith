namespace Content.Server._Exodus.Nebula.Components;

/// <summary>
/// Cached effective linear thrust for a shuttle currently affected by nebula thrust rules.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaShuttleThrustCacheComponent : Component
{
    [ViewVariables]
    public readonly float[] EffectiveLinearThrust = new float[4];

    [ViewVariables]
    public bool Dirty = true;
}
