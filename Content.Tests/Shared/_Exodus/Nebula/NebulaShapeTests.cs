using System;
using System.Numerics;
using Content.Shared._Exodus.Nebula;
using NUnit.Framework;

namespace Content.Tests.Shared._Exodus.Nebula;

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
    public void GeneratorCreatesCompleteNonOverlappingSet()
    {
        var protectedAreas = new[]
        {
            new NebulaProtectedArea(Vector2.Zero, 1_000f),
            new NebulaProtectedArea(new Vector2(8000f, 8000f), 1_000f),
            new NebulaProtectedArea(new Vector2(-8000f, 8000f), 1_000f),
            new NebulaProtectedArea(new Vector2(8000f, -8000f), 1_000f),
        };

        var settings = new NebulaGenerationSettings();
        var result = NebulaGenerator.Generate(12345, protectedAreas, settings);

        Assert.Multiple(() =>
        {
            Assert.That(result.Complete, Is.True);
            Assert.That(result.RequestedCount, Is.InRange(settings.MinCount, settings.MaxCount));
            Assert.That(result.Nebulas, Has.Count.EqualTo(result.RequestedCount));
            Assert.That(result.NebulaTypes, Has.Count.EqualTo(result.Nebulas.Count));
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

    private static Vector2 Rotate(Vector2 vector, float rotation)
    {
        var cos = MathF.Cos(rotation);
        var sin = MathF.Sin(rotation);

        return new Vector2(
            vector.X * cos - vector.Y * sin,
            vector.X * sin + vector.Y * cos);
    }
}
