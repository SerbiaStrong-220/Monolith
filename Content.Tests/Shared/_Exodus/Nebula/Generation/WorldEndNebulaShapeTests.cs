using System;
using System.Numerics;
using Content.Shared._Exodus.Nebula.Generation;
using NUnit.Framework;
using Robust.Shared.Random;

namespace Content.Tests.Shared._Exodus.Nebula.Generation;

[TestFixture]
[TestOf(typeof(WorldEndNebulaShape))]
public sealed class WorldEndNebulaShapeTests
{
    private const float InnerRadius = 75_000f;
    private const float MidRadius = 90_000f;
    private const float Tolerance = 1f;

    [Test]
    public void GenerateMinimumBoundaryRadiusKeepsClearance()
    {
        var shape = WorldEndNebulaShape.Generate(12345, InnerRadius, MidRadius);

        Assert.Multiple(() =>
        {
            Assert.That(shape.IsGenerated, Is.True);
            Assert.That(shape.InnerBoundingRadius, Is.GreaterThanOrEqualTo(InnerRadius * 1.04f - Tolerance));
        });
    }

    [Test]
    public void ContainsRejectsPointsInsideInnerBoundingRadius()
    {
        var shape = WorldEndNebulaShape.Generate(12345, InnerRadius, MidRadius);
        var point = new Vector2(shape.InnerBoundingRadius - 1f, 0f);

        Assert.That(shape.Contains(point), Is.False);
    }

    [Test]
    public void ContainsAcceptsPointsBeyondBoundary()
    {
        var shape = WorldEndNebulaShape.Generate(12345, InnerRadius, MidRadius);

        for (var i = 0; i < 8; i++)
        {
            var theta = MathF.Tau * i / 8f;
            var direction = new Vector2(MathF.Cos(theta), MathF.Sin(theta));
            var point = shape.GetBoundaryPoint(theta) + direction * 10f;

            Assert.That(shape.Contains(point), Is.True);
        }
    }

    [Test]
    public void TryGetZoneReturnsInnerBetweenBoundaryAndMidRadius()
    {
        var shape = WorldEndNebulaShape.Generate(12345, InnerRadius, MidRadius);
        var theta = 0.7f;
        var direction = new Vector2(MathF.Cos(theta), MathF.Sin(theta));
        var point = shape.GetBoundaryPoint(theta) + direction * 100f;

        Assert.Multiple(() =>
        {
            Assert.That(shape.TryGetZone(point, out var zone), Is.True);
            Assert.That(zone, Is.EqualTo(WorldEndZone.Inner));
        });
    }

    [Test]
    public void TryGetZoneReturnsOuterBeyondMidRadius()
    {
        var shape = WorldEndNebulaShape.Generate(12345, InnerRadius, MidRadius);
        var theta = 1.3f;
        var point = new Vector2(MathF.Cos(theta), MathF.Sin(theta)) * (MidRadius + 1_000f);

        Assert.Multiple(() =>
        {
            Assert.That(shape.TryGetZone(point, out var zone), Is.True);
            Assert.That(zone, Is.EqualTo(WorldEndZone.Outer));
        });
    }

    [Test]
    public void TryGetZoneReturnsFalseForDefaultShape()
    {
        var shape = default(WorldEndNebulaShape);

        Assert.That(shape.TryGetZone(Vector2.Zero, out _), Is.False);
    }

    [Test]
    public void TryGetRandomPointInnerSamplesInsideInnerZone()
    {
        var shape = WorldEndNebulaShape.Generate(12345, InnerRadius, MidRadius);
        var rng = new RobustRandom();
        rng.SetSeed(12345);

        for (var i = 0; i < 64; i++)
        {
            Assert.That(shape.TryGetRandomPoint(rng, WorldEndZone.Inner, out var point, 256), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(shape.TryGetZone(point, out var zone), Is.True);
                Assert.That(zone, Is.EqualTo(WorldEndZone.Inner));
            });
        }
    }

    [Test]
    public void TryGetRandomPointOuterReturnsFalseAtDefaultParams()
    {
        var shape = WorldEndNebulaShape.Generate(12345, InnerRadius, MidRadius);
        var rng = new RobustRandom();
        rng.SetSeed(12345);

        Assert.That(shape.TryGetRandomPoint(rng, WorldEndZone.Outer, out _, 256), Is.False);
    }
}
