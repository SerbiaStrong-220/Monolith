using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Exodus.Nebula.Prototypes;

/// <summary>
/// Describes a grid (a "point of interest") that can spawn inside specific nebula kinds at the
/// start of a round. The spawner system distributes copies across matching nebulas at random,
/// subject to <see cref="DuplicateAllowed"/> and placement constraints in the spawn system.
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
    /// Whether two copies of this POI may share one nebula. Default false; nebulas that
    /// already hold this POI id are excluded from the pick pool when this is false.
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

    /// <summary>
    /// Optional maximum distance from the map center where this POI may spawn. When unset,
    /// the POI may spawn anywhere inside a matching nebula.
    /// </summary>
    [DataField]
    public float? MaxSpawnDistanceFromCenter { get; private set; }

    /// <summary>
    /// Optional display name for the loaded grid. When set, replaces the default name derived
    /// from the YAML filename.
    /// </summary>
    [DataField]
    public string? Name { get; private set; }

    /// <summary>
    /// Components attached to the loaded grid entity after spawn. Lets POIs carry IFF colors,
    /// faction markers, power configs, etc. — same shape as
    /// <c>PointOfInterestPrototype.AddComponents</c>.
    /// </summary>
    [DataField]
    public ComponentRegistry AddComponents { get; private set; } = new();

    /// <summary>
    /// Optional <see cref="GameMapPrototype"/> id used to register the loaded grid as a station
    /// via <c>StationSystem.InitializeNewStation</c>. Leave null for plain decorative grids;
    /// set this when the POI hosts station-aware machinery (shipyard consoles, cargo bounty
    /// terminals, anything that asks for <c>GetOwningStation</c>).
    ///
    /// The referenced game map prototype must have a <c>stations</c> entry whose key matches
    /// the prototype's own id (same convention as <c>PointOfInterestSystem.TrySpawnPoiGrid</c>).
    /// </summary>
    [DataField]
    public string? StationGameMap { get; private set; }

    /// <summary>
    /// If true and the POI is registered as a station via <see cref="StationGameMap"/>, its
    /// warp points are hidden from non-admin players. Mirrors
    /// <c>PointOfInterestPrototype.HideWarp</c>.
    /// </summary>
    [DataField]
    public bool HideWarp { get; private set; }
}
