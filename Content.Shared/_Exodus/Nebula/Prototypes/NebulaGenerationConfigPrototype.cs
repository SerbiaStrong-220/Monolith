using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Prototypes;

/// <summary>
/// All tunable parameters of the round-start nebula generator. Resolved by
/// <c>NebulaRoundstartGenerationSystem</c> after regular round-start station/POI generation.
/// Edit the YAML to rebalance nebula counts, sizes, shapes, marker weights, and death-zone
/// radii and shape without C#.
/// </summary>
[Prototype("nebulaGenerationConfig")]
public sealed partial class NebulaGenerationConfigPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = null!;

    // ─── Area budget ───
    /// <summary>Lower bound for a single nebula's area, in world tiles².</summary>
    [DataField] public float MinArea = 13_000_000f;

    /// <summary>Upper bound for a single nebula's area, in world tiles².</summary>
    [DataField] public float MaxArea = 300_000_000f;

    /// <summary>
    /// Total area budget across all blob nebulas. Generation stops adding nebulas once this
    /// is exceeded.
    /// </summary>
    [DataField] public double MaxTotalArea = 8_000_000_000d;

    // ─── Shape ───
    [DataField] public float MinStretch = 0.65f;
    [DataField] public float MaxStretch = 2.1f;
    [DataField] public float MinPower = 1.15f;
    [DataField] public float MaxPower = 2.25f;

    // ─── Boundary waves ───
    [DataField] public float MinWaveAmplitude = 0.02f;
    [DataField] public float MaxWaveAmplitude = 0.12f;
    [DataField] public int MinWaveFrequency = 2;
    [DataField] public int MaxWaveFrequency = 11;

    // ─── Placement ───
    /// <summary>Radius from origin within which nebulas may spawn (world tiles).</summary>
    [DataField] public float CoordinateLimit = 75_000f;

    /// <summary>Padding added around protected stations / fixed safe spots.</summary>
    [DataField] public float ProtectedRadius = 1_000f;

    /// <summary>Minimum gap between two nebulas. 0 allows them to touch.</summary>
    [DataField] public float Separation = 0f;

    /// <summary>Hard cap on candidate placements before generation stops.</summary>
    [DataField] public int MaxAttempts = 16_384;

    /// <summary>Boundary discretisation. Same purpose as <see cref="NebulaShape.DefaultSampleCount"/>.</summary>
    [DataField] public int SampleCount = 512;

    // ─── Death zone ───
    /// <summary>Inner edge of the death-zone ring, in world tiles. Should be ≥ <see cref="CoordinateLimit"/>.</summary>
    [DataField] public float WorldEndInnerRadius = 75_000f;

    /// <summary>Concentric radius that splits death zone into inner / outer sub-zones.</summary>
    [DataField] public float WorldEndMidRadius = 90_000f;

    /// <summary>Lower bound for death-zone boundary wave amplitude.</summary>
    [DataField] public float WorldEndMinWaveAmplitude = 0.002f;

    /// <summary>Upper bound for death-zone boundary wave amplitude.</summary>
    [DataField] public float WorldEndMaxWaveAmplitude = 0.006f;

    /// <summary>Minimum death-zone boundary radius multiplier over <see cref="WorldEndInnerRadius"/>.</summary>
    [DataField] public float WorldEndClearanceMultiplier = 1.04f;

    /// <summary>Death-zone boundary wave frequencies.</summary>
    [DataField] public List<int> WorldEndWaveFrequencies = new() { 3, 5, 7, 11 };

    // ─── Marker housekeeping ───
    /// <summary>
    /// Marker pool with weights. Higher weight = more frequent picks.
    /// The defaults below are NOT used in production (the YAML entry is required and overrides
    /// them); they exist so the safe fallback in
    /// <c>NebulaGenerationSystem.ResolveConfig</c> still produces a working pool when the YAML
    /// is missing or malformed, instead of spawning empty markers.
    /// </summary>
    [DataField(required: true)]
    public List<NebulaMarkerWeight> Markers = new()
    {
        new NebulaMarkerWeight { Proto = "NebulaBlueMarker", Weight = 1f },
        new NebulaMarkerWeight { Proto = "NebulaRedMarker", Weight = 1f },
        new NebulaMarkerWeight { Proto = "NebulaGreenMarker", Weight = 1f },
        new NebulaMarkerWeight { Proto = "NebulaPurpleMarker", Weight = 1f },
    };

    /// <summary>Marker prototype spawned for the inner death-zone sub-zone.</summary>
    [DataField] public EntProtoId DeathZoneInnerMarker = "NebulaDeathZoneInnerMarker";

    /// <summary>Marker prototype spawned for the outer death-zone sub-zone.</summary>
    [DataField] public EntProtoId DeathZoneOuterMarker = "NebulaDeathZoneOuterMarker";

    /// <summary>Used when a per-nebula marker entry is missing or invalid.</summary>
    [DataField] public EntProtoId FallbackMarker = "NebulaBlueMarker";

    // ─── Radar / runtime ───
    /// <summary>Max distance from a radar at which nebula blips are visible (world tiles).</summary>
    [DataField] public float RadarMaxDistance = 250_000f;

    /// <summary>Interval at which the generation system rechecks marker entity health.</summary>
    [DataField] public TimeSpan MarkerValidationInterval = TimeSpan.FromSeconds(30);
}
