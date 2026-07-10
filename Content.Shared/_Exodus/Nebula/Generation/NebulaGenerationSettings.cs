using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Generation;

/// <summary>
/// Runtime container that <see cref="NebulaGenerator"/> consumes. In production it is built
/// from <see cref="NebulaGenerationConfigPrototype"/> in
/// <c>NebulaGenerationSystem.ResolveSettings</c>; in tests it can be instantiated directly
/// with the safe defaults below.
/// </summary>
public sealed class NebulaGenerationSettings
{
    public double[] MaxTotalAreaOptions =
    [
        8_000_000_000d,
    ];

    public int MaxAttempts = 16_384;
    public int SampleCount = NebulaShape.DefaultSampleCount;

    public float MinArea = 13_000_000f;
    public float MaxArea = 300_000_000f;
    public float CoordinateLimit = 75_000f;
    public float ProtectedRadius = 1_000f;
    public float Separation = 0f;

    public float MinStretch = 0.65f;
    public float MaxStretch = 2.1f;
    public float MinPower = 1.15f;
    public float MaxPower = 2.25f;

    public float MinWaveAmplitude = 0.02f;
    public float MaxWaveAmplitude = 0.12f;
    public int MinWaveFrequency = 2;
    public int MaxWaveFrequency = 11;

    /// <summary>
    /// Weighted marker pool. Higher weight = more frequent picks. Weight ≤ 0 disables that
    /// entry. Default pool gives the four built-in colors equal weight.
    /// </summary>
    public (EntProtoId Proto, float Weight)[] NebulaPrototypePool =
    [
        ("NebulaBlueMarker", 1f),
        ("NebulaRedMarker", 1f),
        ("NebulaGreenMarker", 1f),
        ("NebulaPurpleMarker", 1f),
    ];
}
