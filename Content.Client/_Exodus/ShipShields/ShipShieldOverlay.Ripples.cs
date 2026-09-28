using System.Numerics;
using Content.Client._Exodus.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Client._Crescent.ShipShields;

public sealed partial class ShipShieldOverlay
{
    private readonly ShipShieldRippleRenderer _rippleRenderer;

    protected override void DisposeBehavior()
    {
        _rippleRenderer.Dispose();
        base.DisposeBehavior();
    }

    private void DrawRipplingShield(
        DrawingHandleBase handle,
        ChainShape chain,
        Transform transform,
        ShipShieldVisualsComponent visuals)
    {
        var rotation = transform.Quaternion2D;
        var localToWorld = new Matrix3x2(rotation.C, rotation.S, -rotation.S, rotation.C,
            transform.Position.X, transform.Position.Y);
        _rippleRenderer.Draw(handle, chain, localToWorld, visuals);
    }
}
