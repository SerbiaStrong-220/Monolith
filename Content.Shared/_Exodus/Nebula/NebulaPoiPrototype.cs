using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Describes a grid (a "point of interest") that can spawn inside specific nebula kinds at the
/// start of a round. The spawner system distributes copies across matching nebulas with a
/// "fill empty first, then random" policy.
/// </summary>
[Prototype("nebulaPoi")]
public sealed partial class NebulaPoiPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = null!;

    /// <summary>YAML grid to load. Loaded via MapLoaderSystem.TryLoadGrid.</summary>
    [DataField(required: true)]
    public ResPath Path { get; private set; } = default!;

    /// <summary>
    /// Nebula marker prototypes this POI may spawn into. The POI will only consider nebulas
    /// whose marker id is in this list. Death-zone markers are valid entries too.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> SpawnIn { get; private set; } = new();

    /// <summary>
    /// Maximum copies of this POI across the whole map. Default 1.
    /// When 1, <see cref="DuplicateAllowed"/> is irrelevant.
    /// </summary>
    [DataField]
    public int MaxCount { get; private set; } = 1;

    /// <summary>
    /// Whether two copies of this POI may share one nebula. Default false; the spawner will
    /// always prefer a nebula that doesn't yet hold this POI when this is false.
    /// </summary>
    [DataField]
    public bool DuplicateAllowed { get; private set; } = false;

    /// <summary>
    /// Radius (in world tiles) around the chosen spawn point that must be free of other grids,
    /// checked via broadphase before the POI is loaded. Also enforced against other POIs
    /// placed by this spawner in the same round. Default 500.
    /// </summary>
    [DataField]
    public float ProtectedRadius { get; private set; } = 500f;

    /// <summary>
    /// Lower bound of <see cref="NebulaShape.GetDensity"/> at the spawn point. Default 0.5
    /// keeps POIs out of the thin outer fringe of blob nebulas. Ignored for death zones.
    /// </summary>
    [DataField]
    public float MinDensity { get; private set; } = 0.5f;

    /// <summary>
    /// Upper bound of <see cref="NebulaShape.GetDensity"/> at the spawn point. Default 1
    /// (the dense core). Ignored for death zones.
    /// </summary>
    [DataField]
    public float MaxDensity { get; private set; } = 1f;
}
