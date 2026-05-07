using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Mathematical shape for the world-end nebula ring: an inverted boundary that
/// contains all points whose distance from <see cref="Center"/> exceeds the
/// pre-sampled polar boundary r(θ).
///
/// Unlike <see cref="NebulaShape"/> (finite blob), this shape covers everything
/// outside the boundary — from ~75 000 tiles to infinity. The boundary itself is
/// randomised each round via a four-harmonic sinusoidal formula so the entry zone
/// has the same natural irregularity as regular nebulas.
///
/// Boundary formula: B(θ) = 1 + Σ Aᵢ·sin(fᵢ·θ + φᵢ),  f = {3,5,7,11}
/// After RMS-normalisation: r(θ) = R_base · B(θ) / √⟨B²⟩
/// R_base is chosen so that min r(θ) == innerRadius exactly.
/// </summary>
[Serializable, NetSerializable]
public readonly struct WorldEndNebulaShape
{
    public const int SampleCount = 512;
    private const float FadeZone = 3000f;

    private static readonly int[] WaveFrequencies = { 3, 5, 7, 11 };

    public readonly Vector2 Center;
    public readonly float InnerBoundingRadius;
    public readonly float OuterBoundingRadius;

    // Pre-sampled boundary radii; index = (int)(theta / Tau * SampleCount) % SampleCount
    private readonly float[] _boundary;

    private WorldEndNebulaShape(Vector2 center, float[] boundary, float inner, float outer)
    {
        Center = center;
        _boundary = boundary;
        InnerBoundingRadius = inner;
        OuterBoundingRadius = outer;
    }

    /// <summary>
    /// Generates a world-end nebula shape centred at <paramref name="center"/> whose
    /// inner boundary is guaranteed to be at least <paramref name="innerRadius"/> tiles
    /// from the center at every angle.
    /// </summary>
    public static WorldEndNebulaShape Generate(
        int seed,
        float innerRadius,
        Vector2 center = default,
        int samples = SampleCount)
    {
        var rng = new System.Random(seed);

        var amplitudes = new float[4];
        var phases = new float[4];

        for (var i = 0; i < 4; i++)
        {
            amplitudes[i] = (float)(rng.NextDouble() * 0.02 + 0.01); // [0.01, 0.03]
            phases[i] = (float)(rng.NextDouble() * MathF.Tau);
        }

        // Pass 1: compute B(θ) samples and RMS normalisation factor.
        var bSamples = new float[samples];
        var meanSquare = 0f;

        for (var i = 0; i < samples; i++)
        {
            var theta = MathF.Tau * i / samples;
            var b = 1f;

            for (var w = 0; w < 4; w++)
                b += amplitudes[w] * MathF.Sin(WaveFrequencies[w] * theta + phases[w]);

            bSamples[i] = b;
            meanSquare += b * b;
        }

        meanSquare /= samples;
        var normalization = MathF.Sqrt(meanSquare);

        // Pass 2: find minimum normalised B to derive R_base.
        var minNormB = float.MaxValue;

        for (var i = 0; i < samples; i++)
        {
            var normB = bSamples[i] / normalization;
            if (normB < minNormB)
                minNormB = normB;
        }

        var rBase = innerRadius / minNormB;

        // Pass 3: compute final boundary radii.
        var boundary = new float[samples];
        var innerBound = float.MaxValue;
        var outerBound = 0f;

        for (var i = 0; i < samples; i++)
        {
            var r = rBase * bSamples[i] / normalization;
            boundary[i] = r;

            if (r < innerBound) innerBound = r;
            if (r > outerBound) outerBound = r;
        }

        return new WorldEndNebulaShape(center, boundary, innerBound, outerBound);
    }

    /// <summary>
    /// True if <paramref name="point"/> is outside the boundary, i.e. inside the world-end zone.
    /// </summary>
    public bool Contains(Vector2 point)
    {
        var delta = point - Center;
        var r = delta.Length();

        if (r < InnerBoundingRadius)
            return false;

        var index = ThetaToIndex(MathF.Atan2(delta.Y, delta.X));
        return r > _boundary[index];
    }

    /// <summary>
    /// Returns 0 at the boundary, approaches 1 over <see cref="FadeZone"/> tiles inside the zone.
    /// </summary>
    public float GetDensity(Vector2 point)
    {
        var delta = point - Center;
        var r = delta.Length();
        var index = ThetaToIndex(MathF.Atan2(delta.Y, delta.X));
        var boundary = _boundary[index];

        return Math.Clamp((r - boundary) / FadeZone, 0f, 1f);
    }

    public float GetAlpha(Vector2 point)
    {
        return GetDensity(point);
    }

    /// <summary>
    /// Returns the world-space boundary point at angle <paramref name="theta"/>.
    /// Used by debug visualisation.
    /// </summary>
    public Vector2 GetBoundaryPoint(float theta)
    {
        var index = ThetaToIndex(theta);
        var r = _boundary[index];
        return Center + new Vector2(r * MathF.Cos(theta), r * MathF.Sin(theta));
    }

    private int ThetaToIndex(float theta)
    {
        if (theta < 0f)
            theta += MathF.Tau;

        return (int)(theta / MathF.Tau * _boundary.Length) % _boundary.Length;
    }
}
