using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Configures an actor's ability to grow a physical territory claim source.
/// </summary>
[RegisterComponent]
public sealed partial class GrowTerritoryCoreComponent : Component
{
    /// <summary>
    /// Unanchored core to spawn and then anchor after the action completes.
    /// Must have TerritoryBanner and TerritoryCore components.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Core;

    /// <summary>
    /// Uninterrupted time required to grow a core.
    /// </summary>
    [DataField]
    public TimeSpan GrowDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Maximum distance from the actor to the selected position.
    /// </summary>
    [DataField]
    public float Range = 1.5f;

    /// <summary>
    /// Whether growing this core requires the same SRD integrity as placing a normal banner.
    /// </summary>
    [DataField]
    public bool RequireRepairIntegrity = true;
}
