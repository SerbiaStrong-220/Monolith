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
        var requestedCount = random.Next(settings.MinCount, settings.MaxCount + 1);
        result.RequestedCount = requestedCount;
        var maxAttempts = requestedCount * settings.MaxAttemptsPerNebula;

        while (result.Nebulas.Count < requestedCount && result.Attempts < maxAttempts)
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
            result.NebulaTypes.Add(NebulaTypeHelpers.GetRandomNebulaType(random));
        }

        return result;
    }

    public static bool IsInsideCoordinateLimit(NebulaShape shape, float coordinateLimit)
    {
        return MathF.Abs(shape.Center.X) + shape.BoundingRadius <= coordinateLimit &&
            MathF.Abs(shape.Center.Y) + shape.BoundingRadius <= coordinateLimit;
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

        var center = new Vector2(
            NextFloat(random, -limit, limit),
            NextFloat(random, -limit, limit));

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
        return settings.MinCount >= 0 &&
            settings.MaxCount >= settings.MinCount &&
            settings.MaxAttemptsPerNebula > 0 &&
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
}
