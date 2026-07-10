namespace Content.Server._Exodus.NPC;

/// <summary>
/// Runtime list of ship NPC targets temporarily skipped because the current ship weapons cannot fire at them safely.
/// </summary>
[RegisterComponent]
public sealed partial class ShipNpcUnavailableTargetsComponent : Component
{
    public readonly Dictionary<EntityUid, TimeSpan> Targets = new();
}
