using System;
using System.Numerics;
using Content.Shared._Exodus.Nebula.Generation;
using Content.Shared._Exodus.Nebula.Prototypes;
using NUnit.Framework;
using Robust.Shared.Random;

namespace Content.Tests.Shared._Exodus.Nebula.Generation;

[TestFixture]
[TestOf(typeof(NebulaShape))]
public sealed class NebulaShapeTests
{
    private const float Tolerance = 0.001f;

    [Test]
    public void RadiusFromAreaMatchesRequiredLimits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NebulaShape.RadiusFromArea(13_000_000f), Is.EqualTo(2034.214f).Within(Tolerance));
            Assert.That(NebulaShape.RadiusFromArea(300_000_000f), Is.EqualTo(9772.050f).Within(Tolerance));
        });
    }

    [Test]
    public void CircularShapeContainsExpectedPoints()
    {
        Assert.That(TryCreateCircle(out var shape), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(shape.Contains(Vector2.Zero), Is.True);
            Assert.That(shape.Contains(new Vector2(499.9f, 0f)), Is.True);
            Assert.That(shape.Contains(new Vector2(500f, 0f)), Is.True);
            Assert.That(shape.Contains(new Vector2(501f, 0f)), Is.False);
        });
    }

    [Test]
    public void CircularShapeDensityAndAlphaMatchFormula()
    {
        Assert.That(TryCreateCircle(out var shape, power: 2f), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(shape.GetDensity(Vector2.Zero), Is.EqualTo(1f).Within(Tolerance));
            Assert.That(shape.GetDensity(new Vector2(250f, 0f)), Is.EqualTo(0.5f).Within(Tolerance));
            Assert.That(shape.GetAlpha(new Vector2(250f, 0f)), Is.EqualTo(0.25f).Within(Tolerance));
            Assert.That(shape.GetDensity(new Vector2(500f, 0f)), Is.EqualTo(0f).Within(Tolerance));
        });
    }

    [Test]
    public void TryGetRandomPointDensityRespectsBounds()
    {
        Assert.That(TryCreateCircle(out var shape), Is.True);
        var rng = new RobustRandom();
        rng.SetSeed(12345);

        for (var i = 0; i < 64; i++)
        {
            Assert.That(shape.TryGetRandomPoint(rng, 0.5f, 1f, out var point, 256), Is.True);

            var density = shape.GetDensity(point);
            Assert.Multiple(() =>
            {
                Assert.That(shape.Contains(point), Is.True);
                Assert.That(density, Is.InRange(0.5f, 1f));
            });
        }
    }

    [Test]
    public void TryGetRandomPointSwapsInvertedDensityRange()
    {
        Assert.That(TryCreateCircle(out var shape), Is.True);
        var rng = new RobustRandom();
        rng.SetSeed(12345);

        Assert.That(shape.TryGetRandomPoint(rng, 0.8f, 0.4f, out var point, 256), Is.True);

        Assert.That(shape.GetDensity(point), Is.InRange(0.4f, 0.8f));
    }

    [Test]
    public void AreaIsNormalizedToBaseRadius()
    {
        var area = 45_000_000f;
        var radius = NebulaShape.RadiusFromArea(area);

        Assert.That(NebulaShape.TryCreate(
            Vector2.Zero,
            0f,
            2.5f,
            radius,
            1.4f,
            new NebulaWave(0.10f, 2f, 0.1f),
            new NebulaWave(0.08f, 5f, 1.3f),
            new NebulaWave(0.06f, 7f, 2.0f),
            new NebulaWave(0.04f, 11f, 0.4f),
            out var shape), Is.True);

        Assert.That(shape.Area, Is.EqualTo(area).Within(4f));
    }

    [Test]
    public void BoundingRadiusCoversSampledBoundary()
    {
        Assert.That(NebulaShape.TryCreate(
            new Vector2(100f, -200f),
            0.7f,
            2f,
            3000f,
            1.2f,
            new NebulaWave(0.15f, 2f, 0f),
            new NebulaWave(0.10f, 3f, 0.5f),
            new NebulaWave(0.06f, 5f, 1.2f),
            new NebulaWave(0.03f, 9f, 2.1f),
            out var shape), Is.True);

        for (var i = 0; i < NebulaShape.DefaultSampleCount; i++)
        {
            var theta = MathF.Tau * i / NebulaShape.DefaultSampleCount;
            var radius = shape.GetRadius(theta);
            var local = new Vector2(
                radius * MathF.Cos(theta) * shape.Stretch,
                radius * MathF.Sin(theta) / shape.Stretch);
            var rotated = Rotate(local, shape.Rotation);
            var distance = rotated.Length();

            Assert.That(distance, Is.LessThanOrEqualTo(shape.BoundingRadius + Tolerance));
        }
    }

    [Test]
    public void InvalidShapeIsRejected()
    {
        Assert.That(NebulaShape.TryCreate(
            Vector2.Zero,
            0f,
            1f,
            1000f,
            1f,
            new NebulaWave(-2f, 1f, MathF.PI / 2f),
            default,
            default,
            default,
            out _), Is.False);
    }

    [Test]
    public void GeneratorCreatesNonOverlappingSetWithinAreaBudget()
    {
        var protectedAreas = new[]
        {
            new NebulaProtectedArea(Vector2.Zero, 1_000f),
            new NebulaProtectedArea(new Vector2(8000f, 8000f), 1_000f),
            new NebulaProtectedArea(new Vector2(-8000f, 8000f), 1_000f),
            new NebulaProtectedArea(new Vector2(8000f, -8000f), 1_000f),
        };

        var settings = new NebulaGenerationSettings
        {
            MaxTotalAreaOptions = [1_000_000_000d],
        };
        var result = NebulaGenerator.Generate(12345, protectedAreas, settings);

        Assert.Multiple(() =>
        {
            Assert.That(result.Complete, Is.True);
            Assert.That(settings.MaxTotalAreaOptions, Does.Contain(result.MaxTotalArea));
            Assert.That(result.TotalArea, Is.GreaterThan(result.MaxTotalArea));
            Assert.That(result.Rejections.AreaLimit, Is.EqualTo(1));
            Assert.That(result.NebulaPrototypes, Has.Count.EqualTo(result.Nebulas.Count));
            Assert.That(result.Nebulas, Is.Not.Empty);
        });

        for (var i = 0; i < result.Nebulas.Count; i++)
        {
            var nebula = result.Nebulas[i];

            Assert.Multiple(() =>
            {
                Assert.That(nebula.Area, Is.InRange(13_000_000f, 300_000_000f));
                Assert.That(NebulaGenerator.IsInsideCoordinateLimit(nebula, 75_000f), Is.True);
                Assert.That(NebulaGenerator.IntersectsProtectedArea(nebula, protectedAreas), Is.False);
            });

            for (var j = i + 1; j < result.Nebulas.Count; j++)
                Assert.That(NebulaGenerator.IntersectsExistingNebula(nebula, new[] { result.Nebulas[j] }, 0f), Is.False);
        }
    }

    [Test]
    public void OverlapCheckAllowsEmptySpaceInsideBoundingCircle()
    {
        Assert.That(TryCreateEllipse(Vector2.Zero, out var existing), Is.True);
        Assert.That(TryCreateEllipse(new Vector2(0f, 1500f), out var candidate), Is.True);

        Assert.That(NebulaGenerator.IntersectsExistingNebula(candidate, new[] { existing }, 0f), Is.False);
    }

    [Test]
    public void OverlapCheckRejectsActualShapeIntersection()
    {
        Assert.That(TryCreateEllipse(Vector2.Zero, out var existing), Is.True);
        Assert.That(TryCreateEllipse(new Vector2(0f, 900f), out var candidate), Is.True);

        Assert.That(NebulaGenerator.IntersectsExistingNebula(candidate, new[] { existing }, 0f), Is.True);
    }

    private static bool TryCreateCircle(out NebulaShape shape, float power = 1f)
    {
        return NebulaShape.TryCreate(
            Vector2.Zero,
            0f,
            1f,
            500f,
            power,
            default,
            default,
            default,
            default,
            out shape);
    }

    private static bool TryCreateEllipse(Vector2 center, out NebulaShape shape)
    {
        return NebulaShape.TryCreate(
            center,
            0f,
            2f,
            1000f,
            1f,
            default,
            default,
            default,
            default,
            out shape);
    }

    private static Vector2 Rotate(Vector2 vector, float rotation)
    {
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);

        return new Vector2(
            vector.X * cos - vector.Y * sin,
            vector.X * sin + vector.Y * cos);
    }
}
