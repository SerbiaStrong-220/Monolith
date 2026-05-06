using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Pure generation settings for Exodus space nebulas.
/// </summary>
public sealed class NebulaGenerationSettings
{
    public double[] MaxTotalAreaOptions =
    [
        8_000_000_000d,
    ];

    public int MaxAttempts = 8_192;
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
    /// Pool of marker entity prototypes that the generator picks from when assigning a kind
    /// to each generated nebula. Default covers the four built-in colors.
    /// </summary>
    public EntProtoId[] NebulaPrototypePool =
    [
        "NebulaBlueMarker",
        "NebulaRedMarker",
        "NebulaGreenMarker",
        "NebulaPurpleMarker",
    ];
}
