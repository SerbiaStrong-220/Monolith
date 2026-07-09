using System.Numerics;
using Robust.Shared.Random;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Nebula.Generation;

/// <summary>
/// Pure mathematical shape for an Exodus space nebula.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct NebulaShape
{
    public const int DefaultSampleCount = 512;

    public readonly Vector2 Center;
    public readonly float Rotation;
    public readonly float Stretch;
    public readonly float BaseRadius;
    public readonly float Power;
    public readonly NebulaWave Wave1;
    public readonly NebulaWave Wave2;
    public readonly NebulaWave Wave3;
    public readonly NebulaWave Wave4;
    public readonly float BoundaryMeanSquare;
    public readonly float BoundingRadius;

    public float Area => MathF.PI * BaseRadius * BaseRadius;

    private NebulaShape(
        Vector2 center,
        float rotation,
        float stretch,
        float baseRadius,
        float power,
        NebulaWave wave1,
        NebulaWave wave2,
        NebulaWave wave3,
        NebulaWave wave4,
        float boundaryMeanSquare,
        float boundingRadius)
    {
        Center = center;
        Rotation = rotation;
        Stretch = stretch;
        BaseRadius = baseRadius;
        Power = power;
        Wave1 = wave1;
        Wave2 = wave2;
        Wave3 = wave3;
        Wave4 = wave4;
        BoundaryMeanSquare = boundaryMeanSquare;
        BoundingRadius = boundingRadius;
    }

    public static float RadiusFromArea(float area)
    {
        return area <= 0f ? 0f : MathF.Sqrt(area / MathF.PI);
    }

    public static bool TryCreate(
        Vector2 center,
        float rotation,
        float stretch,
        float baseRadius,
        float power,
        NebulaWave wave1,
        NebulaWave wave2,
        NebulaWave wave3,
        NebulaWave wave4,
        out NebulaShape shape,
        int sampleCount = DefaultSampleCount)
    {
        shape = default;

        if (!IsValidFinite(stretch) ||
            !IsValidFinite(baseRadius) ||
            !IsValidFinite(power) ||
            stretch <= 0f ||
            baseRadius <= 0f ||
            power <= 0f ||
            sampleCount <= 0)
        {
            return false;
        }

        var meanSquare = 0f;
        var minBoundary = float.MaxValue;

        for (var i = 0; i < sampleCount; i++)
        {
            var theta = GetSampleTheta(i, sampleCount);
            var boundary = GetBoundaryModifier(theta, wave1, wave2, wave3, wave4);

            minBoundary = MathF.Min(minBoundary, boundary);
            meanSquare += boundary * boundary;
        }

        meanSquare /= sampleCount;

        if (minBoundary <= 0f || meanSquare <= 0f || !IsValidFinite(meanSquare))
            return false;

        var boundingRadius = EstimateBoundingRadius(
            stretch,
            baseRadius,
            meanSquare,
            wave1,
            wave2,
            wave3,
            wave4,
            sampleCount);

        if (boundingRadius <= 0f || !IsValidFinite(boundingRadius))
            return false;

        shape = new NebulaShape(
            center,
            rotation,
            stretch,
            baseRadius,
            power,
            wave1,
            wave2,
            wave3,
            wave4,
            meanSquare,
            boundingRadius);

        return true;
    }

    public bool Contains(Vector2 point)
    {
        return Contains(point, 0f);
    }

    public bool Contains(Vector2 point, float padding)
    {
        var (theta, rho) = ToNebulaPolar(point);
        var radius = GetRadius(theta);

        return radius > 0f && rho <= radius + MathF.Max(0f, padding);
    }

    public float GetDensity(Vector2 point)
    {
        var (theta, rho) = ToNebulaPolar(point);
        var radius = GetRadius(theta);

        if (radius <= 0f)
            return 0f;

        return Math.Clamp(1f - rho / radius, 0f, 1f);
    }

    public float GetAlpha(Vector2 point)
    {
        return MathF.Pow(GetDensity(point), Power);
    }

    /// <summary>
    /// Rejection-samples a random world point inside this nebula whose density is in
    /// [<paramref name="minDensity"/>, <paramref name="maxDensity"/>]. Returns false if no
    /// candidate passes within <paramref name="maxAttempts"/> tries.
    ///
    /// Sampling domain is the bounding box [-BoundingRadius .. BoundingRadius]² around
    /// <see cref="Center"/>, so coverage is uniform over the nebula's actual area.
    /// </summary>
    public bool TryGetRandomPoint(IRobustRandom rng, float minDensity, float maxDensity, out Vector2 point, int maxAttempts = 32)
    {
        point = default;

        if (rng == null || BoundingRadius <= 0f || maxAttempts <= 0)
            return false;

        if (minDensity > maxDensity)
            (minDensity, maxDensity) = (maxDensity, minDensity);

        for (var i = 0; i < maxAttempts; i++)
        {
            var dx = (rng.NextFloat() * 2f - 1f) * BoundingRadius;
            var dy = (rng.NextFloat() * 2f - 1f) * BoundingRadius;
            var candidate = Center + new Vector2(dx, dy);

            if (!Contains(candidate))
                continue;

            var density = GetDensity(candidate);
            if (density < minDensity || density > maxDensity)
                continue;

            point = candidate;
            return true;
        }

        return false;
    }

    public float GetRadius(float theta)
    {
        var boundary = GetBoundaryModifier(theta, Wave1, Wave2, Wave3, Wave4);
        return BaseRadius * boundary / MathF.Sqrt(BoundaryMeanSquare);
    }

    public Vector2 GetBoundaryPoint(float theta)
    {
        var radius = GetRadius(theta);
        var local = new Vector2(
            radius * MathF.Cos(theta) * Stretch,
            radius * MathF.Sin(theta) / Stretch);
        var cos = MathF.Cos(Rotation);
        var sin = MathF.Sin(Rotation);
        var rotated = new Vector2(
            local.X * cos - local.Y * sin,
            local.X * sin + local.Y * cos);

        return Center + rotated;
    }

    private (float Theta, float Rho) ToNebulaPolar(Vector2 point)
    {
        var delta = point - Center;
        var cos = MathF.Cos(Rotation);
        var sin = MathF.Sin(Rotation);

        var px = delta.X * cos + delta.Y * sin;
        var py = -delta.X * sin + delta.Y * cos;

        var ex = px / Stretch;
        var ey = py / (1f / Stretch);

        return (MathF.Atan2(ey, ex), MathF.Sqrt(ex * ex + ey * ey));
    }

    private static float EstimateBoundingRadius(
        float stretch,
        float baseRadius,
        float meanSquare,
        NebulaWave wave1,
        NebulaWave wave2,
        NebulaWave wave3,
        NebulaWave wave4,
        int sampleCount)
    {
        var boundingRadius = 0f;
        var normalization = MathF.Sqrt(meanSquare);

        for (var i = 0; i < sampleCount; i++)
        {
            var theta = GetSampleTheta(i, sampleCount);
            var boundary = GetBoundaryModifier(theta, wave1, wave2, wave3, wave4);
            var radius = baseRadius * boundary / normalization;
            var x = radius * MathF.Cos(theta) * stretch;
            var y = radius * MathF.Sin(theta) / stretch;

            boundingRadius = MathF.Max(boundingRadius, MathF.Sqrt(x * x + y * y));
        }

        return boundingRadius;
    }

    private static float GetBoundaryModifier(
        float theta,
        NebulaWave wave1,
        NebulaWave wave2,
        NebulaWave wave3,
        NebulaWave wave4)
    {
        return 1f
            + GetWaveValue(theta, wave1)
            + GetWaveValue(theta, wave2)
            + GetWaveValue(theta, wave3)
            + GetWaveValue(theta, wave4);
    }

    private static float GetWaveValue(float theta, NebulaWave wave)
    {
        return wave.Amplitude * MathF.Sin(wave.Frequency * theta + wave.Phase);
    }

    private static float GetSampleTheta(int index, int sampleCount)
    {
        return MathF.Tau * index / sampleCount;
    }

    private static bool IsValidFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
