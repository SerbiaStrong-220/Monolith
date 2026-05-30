using System.Numerics;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Pure deterministic generator for Exodus space nebulas.
/// </summary>
public static class NebulaGenerator
{
    public static NebulaGenerationResult Generate(
        int seed,
        IReadOnlyList<Vector2> protectedPositions,
        NebulaGenerationSettings? settings = null)
    {
        var protectedAreas = new List<NebulaProtectedArea>(protectedPositions.Count);

        for (var i = 0; i < protectedPositions.Count; i++)
            protectedAreas.Add(new NebulaProtectedArea(protectedPositions[i], settings?.ProtectedRadius ?? new NebulaGenerationSettings().ProtectedRadius));

        return Generate(seed, protectedAreas, settings);
    }

    public static NebulaGenerationResult Generate(
        int seed,
        IReadOnlyList<NebulaProtectedArea> protectedAreas,
        NebulaGenerationSettings? settings = null)
    {
        settings ??= new NebulaGenerationSettings();
        var result = new NebulaGenerationResult();

        if (!IsValid(settings))
        {
            result.Rejections.InvalidSettings++;
            return result;
        }

        var random = new global::System.Random(seed);
        var maxTotalArea = settings.MaxTotalAreaOptions[random.Next(settings.MaxTotalAreaOptions.Length)];
        result.MaxTotalArea = maxTotalArea;
        result.MaxAttempts = settings.MaxAttempts;

        while (result.Attempts < settings.MaxAttempts)
        {
            result.Attempts++;

            if (!TryCreateCandidate(random, settings, out var candidate))
            {
                result.Rejections.InvalidShape++;
                continue;
            }

            if (!TryPlaceCandidate(random, settings, candidate, out candidate))
            {
                result.Rejections.OutOfBounds++;
                continue;
            }

            if (IntersectsProtectedArea(candidate, protectedAreas))
            {
                result.Rejections.ProtectedArea++;
                continue;
            }

            if (IntersectsExistingNebula(candidate, result.Nebulas, settings.Separation))
            {
                result.Rejections.Overlap++;
                continue;
            }

            result.Nebulas.Add(candidate);
            result.NebulaPrototypes.Add(PickRandomPrototype(random, settings));
            result.TotalArea += candidate.Area;

            if (result.TotalArea <= maxTotalArea)
                continue;

            result.Rejections.AreaLimit++;
            result.HitAreaLimit = true;
            break;
        }

        return result;
    }

    public static bool IsInsideCoordinateLimit(NebulaShape shape, float coordinateLimit)
    {
        return shape.Center.Length() + shape.BoundingRadius <= coordinateLimit;
    }

    public static bool IntersectsProtectedArea(NebulaShape shape, IReadOnlyList<Vector2> protectedPositions, float protectedRadius)
    {
        for (var i = 0; i < protectedPositions.Count; i++)
        {
            if (IntersectsProtectedArea(shape, new NebulaProtectedArea(protectedPositions[i], protectedRadius)))
                return true;
        }

        return false;
    }

    public static bool IntersectsProtectedArea(NebulaShape shape, IReadOnlyList<NebulaProtectedArea> protectedAreas)
    {
        for (var i = 0; i < protectedAreas.Count; i++)
        {
            if (IntersectsProtectedArea(shape, protectedAreas[i]))
                return true;
        }

        return false;
    }

    public static bool IntersectsProtectedArea(NebulaShape shape, NebulaProtectedArea protectedArea)
    {
        var distance = Vector2.Distance(shape.Center, protectedArea.Position);
        return distance < shape.BoundingRadius + protectedArea.Radius;
    }

    public static bool IntersectsExistingNebula(NebulaShape shape, IReadOnlyList<NebulaShape> existing, float separation)
    {
        for (var i = 0; i < existing.Count; i++)
        {
            var distance = Vector2.Distance(shape.Center, existing[i].Center);
            if (distance < shape.BoundingRadius + existing[i].BoundingRadius + separation)
                return true;
        }

        return false;
    }

    private static bool TryCreateCandidate(global::System.Random random, NebulaGenerationSettings settings, out NebulaShape shape)
    {
        var area = NextFloat(random, settings.MinArea, settings.MaxArea);
        var radius = NebulaShape.RadiusFromArea(area);
        var stretch = NextFloat(random, settings.MinStretch, settings.MaxStretch);
        var rotation = NextFloat(random, 0f, MathF.Tau);
        var power = NextFloat(random, settings.MinPower, settings.MaxPower);

        return NebulaShape.TryCreate(
            Vector2.Zero,
            rotation,
            stretch,
            radius,
            power,
            CreateWave(random, settings),
            CreateWave(random, settings),
            CreateWave(random, settings),
            CreateWave(random, settings),
            out shape,
            settings.SampleCount);
    }

    private static bool TryPlaceCandidate(
        global::System.Random random,
        NebulaGenerationSettings settings,
        NebulaShape candidate,
        out NebulaShape placed)
    {
        placed = default;

        var limit = settings.CoordinateLimit - candidate.BoundingRadius;
        if (limit <= 0f)
            return false;

        // Uniform distribution over a disk: r = limit * sqrt(U) avoids centre concentration.
        var angle = (float)(random.NextDouble() * MathF.Tau);
        var r = limit * MathF.Sqrt((float)random.NextDouble());
        var center = new Vector2(r * MathF.Cos(angle), r * MathF.Sin(angle));

        return NebulaShape.TryCreate(
            center,
            candidate.Rotation,
            candidate.Stretch,
            candidate.BaseRadius,
            candidate.Power,
            candidate.Wave1,
            candidate.Wave2,
            candidate.Wave3,
            candidate.Wave4,
            out placed,
            settings.SampleCount);
    }

    private static NebulaWave CreateWave(global::System.Random random, NebulaGenerationSettings settings)
    {
        var amplitude = NextFloat(random, settings.MinWaveAmplitude, settings.MaxWaveAmplitude);
        var frequency = random.Next(settings.MinWaveFrequency, settings.MaxWaveFrequency + 1);
        var phase = NextFloat(random, 0f, MathF.Tau);

        return new NebulaWave(amplitude, frequency, phase);
    }

    private static bool IsValid(NebulaGenerationSettings settings)
    {
        if (settings.MaxTotalAreaOptions == null || settings.MaxTotalAreaOptions.Length == 0)
            return false;

        for (var i = 0; i < settings.MaxTotalAreaOptions.Length; i++)
        {
            if (settings.MaxTotalAreaOptions[i] <= 0d)
                return false;
        }

        return settings.MaxAttempts > 0 &&
            settings.SampleCount > 0 &&
            settings.MinArea > 0f &&
            settings.MaxArea >= settings.MinArea &&
            settings.CoordinateLimit > 0f &&
            settings.ProtectedRadius >= 0f &&
            settings.Separation >= 0f &&
            settings.MinStretch > 0f &&
            settings.MaxStretch >= settings.MinStretch &&
            settings.MinPower > 0f &&
            settings.MaxPower >= settings.MinPower &&
            settings.MinWaveAmplitude >= 0f &&
            settings.MaxWaveAmplitude >= settings.MinWaveAmplitude &&
            settings.MinWaveFrequency > 0 &&
            settings.MaxWaveFrequency >= settings.MinWaveFrequency;
    }

    private static float NextFloat(global::System.Random random, float min, float max)
    {
        return min + (float) random.NextDouble() * (max - min);
    }

    private static Robust.Shared.Prototypes.EntProtoId PickRandomPrototype(global::System.Random random, NebulaGenerationSettings settings)
    {
        var pool = settings.NebulaPrototypePool;
        if (pool == null || pool.Length == 0)
            return default;

        var totalWeight = 0f;
        for (var i = 0; i < pool.Length; i++)
            totalWeight += MathF.Max(0f, pool[i].Weight);

        // No positive weights — fall back to a uniform pick so we never return default.
        if (totalWeight <= 0f)
            return pool[random.Next(pool.Length)].Proto;

        var roll = (float) random.NextDouble() * totalWeight;
        var cumulative = 0f;
        for (var i = 0; i < pool.Length; i++)
        {
            cumulative += MathF.Max(0f, pool[i].Weight);
            if (roll <= cumulative)
                return pool[i].Proto;
        }

        // Floating-point rounding guard — pick the last positive-weight entry.
        for (var i = pool.Length - 1; i >= 0; i--)
        {
            if (pool[i].Weight > 0f)
                return pool[i].Proto;
        }

        return pool[^1].Proto;
    }
}
