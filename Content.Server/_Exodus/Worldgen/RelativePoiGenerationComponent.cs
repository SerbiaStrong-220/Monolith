using Robust.Shared.Prototypes;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Worldgen;

/// <summary>
/// Transient work queue owned by the generating map. Output lists belong to the adventure
/// rule and receive the actual grids when deferred placement succeeds. Cleared at generation end.
/// </summary>
[RegisterComponent]
public sealed partial class RelativePoiGenerationComponent : Component
{
    public Dictionary<PoiSpawnKey, RelativePoiPlacementPrototype> Rules = new();
    public HashSet<PoiSpawnKey> InvalidTargets = new();
    public List<RelativePoiSpawnRequest> Pending = new();
}

public sealed record RelativePoiSpawnRequest(
    ProtoId<RelativePoiPlacementPrototype> Rule,
    List<EntityUid>? Output = null,
    string? OverrideName = null,
    int? DepotIndex = null);

[ByRefEvent]
public readonly record struct RelativePoiSpawnEvent(
    MapId Map,
    RelativePoiPlacementPrototype Rule,
    RelativePoiSpawnRequest Request);

/// <summary>Allows the ordinary spawner to retain reservations for unloaded fixed POIs.</summary>
[ByRefEvent]
public record struct RelativePoiPositionAttemptEvent(
    MapId Map,
    System.Numerics.Vector2 Position,
    System.Numerics.Vector2 AnchorOrigin,
    float Clearance)
{
    public bool Cancelled;
}
