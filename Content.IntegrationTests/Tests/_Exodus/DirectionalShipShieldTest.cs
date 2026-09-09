using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(ShipShieldsSystem))]
public sealed class DirectionalShipShieldTest
{
    [Test]
    public void IncomingDirectionRespectsConfiguredArc()
    {
        const float arcDegrees = 120f;
        var shieldDirection = Angle.Zero;

        Assert.Multiple(() =>
        {
            Assert.That(IsProtectedAtOffset(shieldDirection, arcDegrees, 0f), Is.True);
            Assert.That(IsProtectedAtOffset(shieldDirection, arcDegrees, 59f), Is.True);
            Assert.That(IsProtectedAtOffset(shieldDirection, arcDegrees, -59f), Is.True);
            Assert.That(IsProtectedAtOffset(shieldDirection, arcDegrees, 61f), Is.False);
            Assert.That(IsProtectedAtOffset(shieldDirection, arcDegrees, -61f), Is.False);
            Assert.That(IsProtectedAtOffset(shieldDirection, arcDegrees, 180f), Is.False);
        });
    }

    private static bool IsProtectedAtOffset(Angle shieldDirection, float arcDegrees, float offsetDegrees)
    {
        var incomingDirection = (shieldDirection + Angle.FromDegrees(offsetDegrees)).ToWorldVec();
        var relativeVelocity = -new Vector2(incomingDirection.X, incomingDirection.Y);
        return ShipShieldsSystem.IsIncomingDirectionProtected(shieldDirection, arcDegrees, relativeVelocity);
    }
}
