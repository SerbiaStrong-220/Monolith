using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Prototypes;

/// <summary>
/// Marks a station/POI/grid as protected from nebula generation. The prototype id is matched
/// against PointOfInterestPrototype id, GameMapPrototype id, and BecomesStationComponent.Id;
/// the matched grid's AABB plus the default protected radius is excluded from generation.
/// </summary>
[Prototype("nebulaProtectedStation")]
public sealed partial class NebulaProtectedStationPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = null!;
}
