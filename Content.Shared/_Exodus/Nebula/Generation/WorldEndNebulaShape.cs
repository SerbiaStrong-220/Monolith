using System.Numerics;
using Robust.Shared.Random;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Nebula.Generation;

/// <summary>
/// Mathematical shape for the world-end nebula ring: an inverted boundary that
/// contains all points whose distance from <see cref="Center"/> exceeds the
/// pre-sampled polar boundary r(θ).
///
/// Unlike <see cref="NebulaShape"/> (finite blob), this shape covers everything
/// outside the boundary — from ~75 000 tiles to infinity. The boundary itself is
/// randomised each round via a configurable sinusoidal formula so the entry zone
/// has the same natural irregularity as regular nebulas.
///
/// Boundary formula: B(θ) = 1 + Σ Aᵢ·sin(fᵢ·θ + φᵢ)
/// After RMS-normalisation: r(θ) = R_base · B(θ) / √⟨B²⟩
/// R_base is chosen so that min r(θ) matches the configured clearance boundary.
/// </summary>
[Serializable, NetSerializable]
public readonly struct WorldEndNebulaShape
{
    public const int SampleCount = 512;
    public const float DefaultMinWaveAmplitude = 0.002f;
    public const float DefaultMaxWaveAmplitude = 0.006f;
    public const float DefaultClearanceMultiplier = 1.04f;

    private const int MaxWaveCount = 16;
    private const int MaxWaveFrequency = 64;
    private const int MinSearchSamplesPerBoundarySample = 32;
    private const int MinSearchSamplesPerWaveFrequency = 1536;
    private const float MaxTotalWaveAmplitude = 0.95f;

    private static readonly int[] DefaultWaveFrequencies = { 3, 5, 7, 11 };

    public readonly Vector2 Center;
    public readonly float InnerBoundingRadius;
    public readonly float OuterBoundingRadius;

    /// <summary>
    /// Concentric radius from <see cref="Center"/> that splits the death zone into an inner
    /// sub-zone (boundary..MidRadius) and an outer sub-zone (MidRadius..∞). Used by
    /// <see cref="TryGetZone"/>; FTL blocking ignores this and treats the whole shape as one.
    /// </summary>
    public readonly float MidRadius;

    // Pre-sampled boundary radii; index = (int)(theta / Tau * SampleCount) % SampleCount
    private readonly float[] _boundary;

    public bool IsGenerated => _boundary != null;

    private WorldEndNebulaShape(Vector2 center, float[] boundary, float inner, float outer, float midRadius)
    {
        Center = center;
        _boundary = boundary;
        InnerBoundingRadius = inner;
        OuterBoundingRadius = outer;
        MidRadius = midRadius;
    }

    /// <summary>
    /// Generates a world-end nebula shape centred at <paramref name="center"/> whose
    /// inner boundary is guaranteed to be at least <paramref name="innerRadius"/> tiles
    /// from the center at every angle.
    /// </summary>
    public static WorldEndNebulaShape Generate(
        int seed,
        float innerRadius,
        float midRadius = 0f,
        Vector2 center = default,
        int samples = SampleCount,
        float minWaveAmplitude = DefaultMinWaveAmplitude,
        float maxWaveAmplitude = DefaultMaxWaveAmplitude,
        float clearanceMultiplier = DefaultClearanceMultiplier,
        IReadOnlyList<int>? waveFrequencies = null)
    {
        var rng = new RobustRandom();
        rng.SetSeed(seed);
        samples = Math.Max(1, samples);
        innerRadius = MathF.Max(1f, innerRadius);
        clearanceMultiplier = MathF.Max(1f, IsValidFinite(clearanceMultiplier) ? clearanceMultiplier : DefaultClearanceMultiplier);
        SanitizeWaveAmplitude(ref minWaveAmplitude, ref maxWaveAmplitude);

        var frequencies = ResolveWaveFrequencies(waveFrequencies);
        var amplitudes = new float[frequencies.Length];
        var phases = new float[frequencies.Length];

        for (var i = 0; i < frequencies.Length; i++)
        {
            amplitudes[i] = NextFloat(rng, minWaveAmplitude, maxWaveAmplitude);
            phases[i] = rng.NextFloat() * MathF.Tau;
        }

        NormalizeAmplitudes(amplitudes);

        // Pass 1: compute B(θ) samples and RMS normalisation factor.
        var bSamples = new float[samples];
        var meanSquare = 0f;

        for (var i = 0; i < samples; i++)
        {
            var theta = MathF.Tau * i / samples;
            var b = 1f;

            for (var w = 0; w < frequencies.Length; w++)
                b += amplitudes[w] * MathF.Sin(frequencies[w] * theta + phases[w]);

            bSamples[i] = b;
            meanSquare += b * b;
        }

        meanSquare /= samples;
        var normalization = MathF.Sqrt(meanSquare);

        // Pass 2: find minimum normalised B using a dense search so the continuous minimum
        // between sample points is not missed. A coarse sample search would underestimate
        // rBase and allow the boundary to dip below innerRadius.
        var minSearchSamples = Math.Max(
            samples * MinSearchSamplesPerBoundarySample,
            GetMaxFrequency(frequencies) * MinSearchSamplesPerWaveFrequency);
        var minNormB = float.MaxValue;

        for (var i = 0; i < minSearchSamples; i++)
        {
            var theta = MathF.Tau * i / minSearchSamples;
            var b = 1f;

            for (var w = 0; w < frequencies.Length; w++)
                b += amplitudes[w] * MathF.Sin(frequencies[w] * theta + phases[w]);

            var normB = b / normalization;
            if (normB < minNormB)
                minNormB = normB;
        }

        // Enforce a clearance buffer so the boundary never touches the nebula generation zone.
        // minBoundaryRadius is the guaranteed minimum radius from the configured clearance.
        var minBoundaryRadius = innerRadius * clearanceMultiplier;
        var rBase = minBoundaryRadius / minNormB;

        // Pass 3: compute final boundary radii, clamped to the clearance minimum.
        var boundary = new float[samples];
        var innerBound = float.MaxValue;
        var outerBound = 0f;

        for (var i = 0; i < samples; i++)
        {
            var r = rBase * bSamples[i] / normalization;
            if (r < minBoundaryRadius)
                r = minBoundaryRadius;
            boundary[i] = r;

            if (r < innerBound) innerBound = r;
            if (r > outerBound) outerBound = r;
        }

        return new WorldEndNebulaShape(center, boundary, innerBound, outerBound, midRadius);
    }

    private static int[] ResolveWaveFrequencies(IReadOnlyList<int>? waveFrequencies)
    {
        if (waveFrequencies == null || waveFrequencies.Count == 0)
            return DefaultWaveFrequencies;

        var validCount = 0;
        for (var i = 0; i < waveFrequencies.Count; i++)
        {
            if (waveFrequencies[i] > 0)
                validCount++;

            if (validCount >= MaxWaveCount)
                break;
        }

        if (validCount == 0)
            return DefaultWaveFrequencies;

        var frequencies = new int[validCount];
        var write = 0;
        for (var i = 0; i < waveFrequencies.Count; i++)
        {
            if (waveFrequencies[i] <= 0)
                continue;

            frequencies[write] = Math.Min(waveFrequencies[i], MaxWaveFrequency);
            write++;

            if (write >= validCount)
                break;
        }

        return frequencies;
    }

    private static int GetMaxFrequency(IReadOnlyList<int> frequencies)
    {
        var max = 1;
        for (var i = 0; i < frequencies.Count; i++)
            max = Math.Max(max, frequencies[i]);

        return max;
    }

    private static void SanitizeWaveAmplitude(ref float minWaveAmplitude, ref float maxWaveAmplitude)
    {
        if (!IsValidFinite(minWaveAmplitude))
            minWaveAmplitude = DefaultMinWaveAmplitude;

        if (!IsValidFinite(maxWaveAmplitude))
            maxWaveAmplitude = DefaultMaxWaveAmplitude;

        minWaveAmplitude = MathF.Max(0f, minWaveAmplitude);
        maxWaveAmplitude = MathF.Max(0f, maxWaveAmplitude);

        if (maxWaveAmplitude < minWaveAmplitude)
            (minWaveAmplitude, maxWaveAmplitude) = (maxWaveAmplitude, minWaveAmplitude);
    }

    private static void NormalizeAmplitudes(float[] amplitudes)
    {
        var totalAmplitude = 0f;
        for (var i = 0; i < amplitudes.Length; i++)
            totalAmplitude += amplitudes[i];

        if (totalAmplitude <= MaxTotalWaveAmplitude)
            return;

        var scale = MaxTotalWaveAmplitude / totalAmplitude;
        for (var i = 0; i < amplitudes.Length; i++)
            amplitudes[i] *= scale;
    }

    private static float NextFloat(IRobustRandom rng, float min, float max)
    {
        return min + rng.NextFloat() * (max - min);
    }

    private static bool IsValidFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// True if <paramref name="point"/> is outside the boundary, i.e. inside the world-end zone.
    /// </summary>
    public bool Contains(Vector2 point)
    {
        if (_boundary == null)
            return false;

        var delta = point - Center;
        var r = delta.Length();

        if (r < InnerBoundingRadius)
            return false;

        return r > SampleBoundary(MathF.Atan2(delta.Y, delta.X));
    }

    /// <summary>
    /// Single-pass check: is <paramref name="point"/> inside the death zone, and if so, in
    /// which concentric sub-zone (split by <see cref="MidRadius"/>).
    /// Returns false (zone = default) if outside the death zone or shape ungenerated.
    /// </summary>
    public bool TryGetZone(Vector2 point, out WorldEndZone zone)
    {
        zone = default;
        if (_boundary == null)
            return false;

        var delta = point - Center;
        var rSq = delta.LengthSquared();

        if (rSq < InnerBoundingRadius * InnerBoundingRadius)
            return false;

        var r = MathF.Sqrt(rSq);
        if (r <= SampleBoundary(MathF.Atan2(delta.Y, delta.X)))
            return false;

        zone = MidRadius > 0f && r > MidRadius ? WorldEndZone.Outer : WorldEndZone.Inner;
        return true;
    }

    public float GetDensity(Vector2 point) => 1f;

    public float GetAlpha(Vector2 point) => 1f;

    /// <summary>
    /// Samples a random world point inside the requested concentric sub-zone. Picks a random
    /// angle and a random radial distance within that angle's valid range for the sub-zone
    /// (inner: boundary..MidRadius, outer: max(boundary, MidRadius)..OuterBoundingRadius).
    /// Returns false if no candidate fits within <paramref name="maxAttempts"/> tries — this
    /// only happens for the inner sub-zone on rays where the boundary contour bulges past
    /// <see cref="MidRadius"/>, leaving no inner band on that ray.
    /// </summary>
    public bool TryGetRandomPoint(IRobustRandom rng, WorldEndZone zone, out Vector2 point, int maxAttempts = 32)
    {
        point = default;

        if (rng == null || _boundary == null || maxAttempts <= 0)
            return false;

        for (var i = 0; i < maxAttempts; i++)
        {
            var theta = rng.NextFloat() * MathF.Tau;
            var boundary = SampleBoundary(theta);

            float minR, maxR;
            if (zone == WorldEndZone.Outer)
            {
                minR = MathF.Max(MidRadius, boundary);
                maxR = OuterBoundingRadius;
            }
            else
            {
                minR = boundary;
                maxR = MidRadius > 0f ? MidRadius : OuterBoundingRadius;
            }

            if (maxR <= minR)
                continue;

            var r = minR + rng.NextFloat() * (maxR - minR);
            point = Center + new Vector2(r * MathF.Cos(theta), r * MathF.Sin(theta));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the world-space boundary point at angle <paramref name="theta"/>.
    /// Used by radar visualisation.
    /// </summary>
    public Vector2 GetBoundaryPoint(float theta)
    {
        var r = SampleBoundary(theta);
        return Center + new Vector2(r * MathF.Cos(theta), r * MathF.Sin(theta));
    }

    // Linearly interpolates between the two nearest boundary samples for smooth, accurate lookup.
    private float SampleBoundary(float theta)
    {
        if (theta < 0f)
            theta += MathF.Tau;

        var fIndex = theta / MathF.Tau * _boundary.Length;
        var i0 = (int)fIndex % _boundary.Length;
        var i1 = (i0 + 1) % _boundary.Length;
        var t = fIndex - MathF.Floor(fIndex);
        return _boundary[i0] * (1f - t) + _boundary[i1] * t;
    }
}
