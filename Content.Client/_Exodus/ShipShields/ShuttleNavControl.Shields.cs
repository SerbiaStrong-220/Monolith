using System.Numerics;
using Content.Client._Exodus.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Client.Shuttles.UI;

public partial class ShuttleNavControl
{
    private ShipShieldRippleRenderer? _shieldRippleRenderer;

    private void DrawRipplingShieldOnRadar(
        DrawingHandleScreen handle,
        Entity<ShipShieldVisualsComponent, TransformComponent> shield,
        ChainShape chain,
        Matrix3x2 worldToShuttle)
    {
        var scale = MinimapScale;
        if (scale <= 0f || chain.Vertices.Length < 2)
            return;

        var localToView = _transform.GetWorldMatrix(shield.Comp2, _xformQuery) * worldToShuttle
            * Matrix3x2.CreateScale(scale, -scale) * Matrix3x2.CreateTranslation(MidPointVector);

        var bounds = Box2.FromDimensions(Vector2.Transform(chain.Vertices[0], localToView), Vector2.Zero);
        foreach (var vertex in chain.Vertices)
            bounds = bounds.ExtendToContain(Vector2.Transform(vertex, localToView));

        if (!bounds.Intersects(new Box2(Vector2.Zero, PixelSize)))
            return;

        _shieldRippleRenderer ??= new ShipShieldRippleRenderer(_prototype);
        // Keep the waves readable when zooming out without changing the field's outer boundary.
        var minimumWidth = 8f * UIScale / scale;
        _shieldRippleRenderer.Draw(handle, chain, localToView, shield.Comp1, minimumWidth);
    }

    protected override void ExitedTree()
    {
        _shieldRippleRenderer?.Dispose();
        _shieldRippleRenderer = null;
        base.ExitedTree();
    }
}
