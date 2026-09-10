using System.Numerics;
using Content.Client._Exodus.Territory;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Client._Exodus.Territory;

[TestFixture]
[TestOf(typeof(CorporateTerritoryRingRenderer))]
public sealed class CorporateTerritoryRingRendererTest
{
    [Test]
    public void BorderRemainsVisibleWhenTheTerritoryItselfIsOffscreen()
    {
        var view = new Box2(100f, -10f, 110f, 10f);
        Assert.That(CorporateTerritoryRingRenderer.IntersectsView(Vector2.Zero, 90f, 105f, view), Is.True);
    }

    [Test]
    public void BorderIsCulledWhenTheViewIsEntirelyInsideTheTerritory()
    {
        var view = new Box2(-10f, -10f, 10f, 10f);
        Assert.That(CorporateTerritoryRingRenderer.IntersectsView(Vector2.Zero, 90f, 110f, view), Is.False);
    }

    [Test]
    public void BorderIsCulledWhenTheViewIsOutsideItsOuterEdge()
    {
        var view = new Box2(100f, 100f, 110f, 110f);
        Assert.That(CorporateTerritoryRingRenderer.IntersectsView(Vector2.Zero, 90f, 110f, view), Is.False);
    }

    [Test]
    public void BorderIsVisibleWhenTheTerritoryCenterIsOutsideTheView()
    {
        var view = new Box2(0f, 0f, 200f, 200f);
        Assert.That(CorporateTerritoryRingRenderer.IntersectsView(new Vector2(-50f, 100f), 90f, 110f, view), Is.True);
    }
}
