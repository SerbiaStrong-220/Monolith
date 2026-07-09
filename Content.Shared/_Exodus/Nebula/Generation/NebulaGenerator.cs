using System.Numerics;
using Content.Shared._Exodus.Nebula.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._Exodus.Nebula.Generation;

/// <summary>
/// Pure deterministic generator for Exodus space nebulas.
/// </summary>
public static class NebulaGenerator
{
    private const int ShapeOverlapSampleCount = 96;
    private const float MinSpatialCellSize = 4096f;

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

        var random = new RobustRandom();
        random.SetSeed(seed);
        var maxTotalArea = settings.MaxTotalAreaOptions[random.Next(settings.MaxTotalAreaOptions.Length)];
        result.MaxTotalArea = maxTotalArea;
        result.MaxAttempts = settings.MaxAttempts;

        var spatial = new NebulaSpatialIndex(CreateSpatialCellSize(settings));
        var nearbyNebulas = new List<int>();

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

            if (IntersectsExistingNebula(candidate, result.Nebulas, spatial, nearbyNebulas, settings.Separation))
            {
                result.Rejections.Overlap++;
                continue;
            }

            var nebulaIndex = result.Nebulas.Count;
            result.Nebulas.Add(candidate);
            spatial.Add(candidate, nebulaIndex);
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
            if (IntersectsExistingNebula(shape, existing[i], separation))
                return true;
        }

        return false;
    }

    private static bool IntersectsExistingNebula(
        NebulaShape shape,
        IReadOnlyList<NebulaShape> existing,
        NebulaSpatialIndex spatial,
        List<int> nearbyNebulas,
        float separation)
    {
        spatial.Query(shape, nearbyNebulas, separation);

        for (var i = 0; i < nearbyNebulas.Count; i++)
        {
            var index = nearbyNebulas[i];
            if (index < 0 || index >= existing.Count)
                continue;

            if (IntersectsExistingNebula(shape, existing[index], separation))
                return true;
        }

        return false;
    }

    private static bool IntersectsExistingNebula(NebulaShape shape, NebulaShape existing, float separation)
    {
        var padding = MathF.Max(0f, separation);
        var boundingRadius = shape.BoundingRadius + existing.BoundingRadius + padding;
        var delta = shape.Center - existing.Center;

        if (delta.LengthSquared() >= boundingRadius * boundingRadius)
            return false;

        return ShapesOverlap(shape, existing, padding);
    }

    private static bool ShapesOverlap(NebulaShape first, NebulaShape second, float padding)
    {
        if (first.Contains(second.Center, padding) || second.Contains(first.Center, padding))
            return true;

        for (var i = 0; i < ShapeOverlapSampleCount; i++)
        {
            var theta = MathF.Tau * i / ShapeOverlapSampleCount;

            if (second.Contains(first.GetBoundaryPoint(theta), padding) ||
                first.Contains(second.GetBoundaryPoint(theta), padding))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateCandidate(IRobustRandom random, NebulaGenerationSettings settings, out NebulaShape shape)
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
        IRobustRandom random,
        NebulaGenerationSettings settings,
        NebulaShape candidate,
        out NebulaShape placed)
    {
        placed = default;

        var limit = settings.CoordinateLimit - candidate.BoundingRadius;
        if (limit <= 0f)
            return false;

        // Uniform distribution over a disk: r = limit * sqrt(U) avoids centre concentration.
        var angle = random.NextFloat() * MathF.Tau;
        var r = limit * MathF.Sqrt(random.NextFloat());
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

    private static NebulaWave CreateWave(IRobustRandom random, NebulaGenerationSettings settings)
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

    private static float CreateSpatialCellSize(NebulaGenerationSettings settings)
    {
        return MathF.Max(MinSpatialCellSize, NebulaShape.RadiusFromArea(settings.MaxArea));
    }

    private static float NextFloat(IRobustRandom random, float min, float max)
    {
        return min + random.NextFloat() * (max - min);
    }

    private static Robust.Shared.Prototypes.EntProtoId PickRandomPrototype(IRobustRandom random, NebulaGenerationSettings settings)
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

        var roll = random.NextFloat() * totalWeight;
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

    private readonly record struct SpatialCell(int X, int Y);

    private sealed class NebulaSpatialIndex
    {
        private readonly Dictionary<SpatialCell, List<int>> _cells = new();
        private readonly Dictionary<int, int> _seen = new();
        private readonly float _cellSize;
        private int _stamp;

        public NebulaSpatialIndex(float cellSize)
        {
            _cellSize = MathF.Max(1f, cellSize);
        }

        public void Add(NebulaShape shape, int index)
        {
            GetCellBounds(shape, 0f, out var minX, out var maxX, out var minY, out var maxY);

            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    var cell = new SpatialCell(x, y);
                    if (!_cells.TryGetValue(cell, out var indexes))
                    {
                        indexes = new List<int>();
                        _cells.Add(cell, indexes);
                    }

                    indexes.Add(index);
                }
            }
        }

        public void Query(NebulaShape shape, List<int> results, float padding)
        {
            results.Clear();
            _stamp++;

            GetCellBounds(shape, MathF.Max(0f, padding), out var minX, out var maxX, out var minY, out var maxY);

            for (var x = minX; x <= maxX; x++)
            {
                for (var y = minY; y <= maxY; y++)
                {
                    if (!_cells.TryGetValue(new SpatialCell(x, y), out var indexes))
                        continue;

                    for (var i = 0; i < indexes.Count; i++)
                    {
                        var index = indexes[i];
                        if (_seen.TryGetValue(index, out var seenStamp) && seenStamp == _stamp)
                            continue;

                        _seen[index] = _stamp;
                        results.Add(index);
                    }
                }
            }
        }

        private void GetCellBounds(
            NebulaShape shape,
            float padding,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY)
        {
            var radius = shape.BoundingRadius + padding;
            minX = GetCell(shape.Center.X - radius);
            maxX = GetCell(shape.Center.X + radius);
            minY = GetCell(shape.Center.Y - radius);
            maxY = GetCell(shape.Center.Y + radius);
        }

        private int GetCell(float value)
        {
            return (int) MathF.Floor(value / _cellSize);
        }
    }
}
