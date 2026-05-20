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

    private static readonly int[] WaveFrequencies = { 3, 5, 7, 11 };

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
        int samples = SampleCount)
    {
        var rng = new System.Random(seed);

        var amplitudes = new float[4];
        var phases = new float[4];

        for (var i = 0; i < 4; i++)
        {
            amplitudes[i] = (float)(rng.NextDouble() * 0.004 + 0.002); // [0.002, 0.006]
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

        // Pass 2: find minimum normalised B using a dense search so the continuous minimum
        // between sample points is not missed. A coarse sample search would underestimate
        // rBase and allow the boundary to dip below innerRadius.
        const int minSearchSamples = SampleCount * 32; // 16 384 — ~1500 points per highest-freq cycle
        var minNormB = float.MaxValue;

        for (var i = 0; i < minSearchSamples; i++)
        {
            var theta = MathF.Tau * i / minSearchSamples;
            var b = 1f;

            for (var w = 0; w < 4; w++)
                b += amplitudes[w] * MathF.Sin(WaveFrequencies[w] * theta + phases[w]);

            var normB = b / normalization;
            if (normB < minNormB)
                minNormB = normB;
        }

        // Enforce a clearance buffer so the boundary never touches the nebula generation zone.
        // minBoundaryRadius is the guaranteed minimum radius — 4 % above innerRadius.
        var minBoundaryRadius = innerRadius * 1.04f;
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
    public bool TryGetRandomPoint(System.Random rng, WorldEndZone zone, out Vector2 point, int maxAttempts = 32)
    {
        point = default;

        if (rng == null || _boundary == null || maxAttempts <= 0)
            return false;

        for (var i = 0; i < maxAttempts; i++)
        {
            var theta = (float) (rng.NextDouble() * MathF.Tau);
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

            var r = minR + (float) rng.NextDouble() * (maxR - minR);
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
