namespace Content.Server._Exodus.Territory;

/// <summary>
/// Marks a RadarBlipComponent that was added by the territory banner claim system.
/// Used so cleanup does not remove unrelated radar signatures from the same entity.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveTerritoryBannerRadarBlipComponent : Component
{
    public EntityUid Grid;

    public bool Removing;
}
