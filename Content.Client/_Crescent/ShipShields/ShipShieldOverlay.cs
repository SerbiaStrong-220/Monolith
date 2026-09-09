using Content.Shared._Crescent.ShipShields;
using Robust.Client.ResourceManagement;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Physics;
using Robust.Shared.Prototypes;
using System.Runtime.InteropServices;
using Robust.Client.GameObjects;

namespace Content.Client._Crescent.ShipShields;

public sealed class ShipShieldOverlay : Overlay
{
    private readonly FixtureSystem _fixture;
    private readonly SharedPhysicsSystem _physics;
    private readonly IResourceCache _resourceCache;
    private readonly IEntityManager _entManager;
    private readonly ShaderInstance _unshadedShader;
    private readonly List<DrawVertexUV2D> _verts = new(128); // Mono
    private readonly Texture _shieldTexture;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public ShipShieldOverlay(IEntityManager entityManager, IPrototypeManager prototypeManager, IResourceCache resourceCache)
    {
        _resourceCache = resourceCache;
        _entManager = entityManager;
        _fixture = _entManager.EntitySysManager.GetEntitySystem<FixtureSystem>();
        _physics = _entManager.EntitySysManager.GetEntitySystem<Robust.Client.Physics.PhysicsSystem>();
        _shieldTexture = _resourceCache.GetTexture("/Textures/_Crescent/ShipShields/shieldtex.png");

        _unshadedShader = prototypeManager.Index<ShaderPrototype>("unshaded").Instance();

        ZIndex = 8;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;

        handle.UseShader(_unshadedShader);

        var enumerator = _entManager.AllEntityQueryEnumerator<ShipShieldVisualsComponent, FixturesComponent, TransformComponent>();
        while (enumerator.MoveNext(out var uid, out var visuals, out var fixtures, out var xform))
        {

            if (xform.MapID != args.MapId)
                continue;

            var fixture = _fixture.GetFixtureOrNull(uid, "shield", fixtures);

            if (fixture is not { Shape: ChainShape chain })
                continue;

            // Mono: No need to draw the shield locally if its out of range.
            var transform = _physics.GetPhysicsTransform(uid, xform);
            var worldBounds = new Box2();
            foreach (var vertex in chain.Vertices)
            {
                var worldPos = VertexToWorldPos(vertex, transform);
                worldBounds = worldBounds.ExtendToContain(worldPos);
            }
            if (!args.WorldAABB.Intersects(worldBounds))
                continue;

            DrawShield(handle, chain, transform, _shieldTexture, visuals, _verts); // Exodus layered ship shield visuals
        }
    }

    // Exodus-begin layered ship shield visuals
    private void DrawShield(
        DrawingHandleWorld handle,
        ChainShape chain,
        Transform transform,
        Texture tex,
        ShipShieldVisualsComponent visuals,
        List<DrawVertexUV2D> verts)
    {
        var layerCount = Math.Max(1, visuals.LayerCount);
        var layerThickness = Math.Max(0.05f, visuals.LayerThickness);
        var layerGap = Math.Max(0f, visuals.LayerGap);

        for (var layer = 0; layer < layerCount; layer++)
        {
            var outerOffset = layer * (layerThickness + layerGap);
            var innerOffset = outerOffset + layerThickness;
            var layerColor = GetLayerColor(visuals.ShieldColor, layer, layerCount);

            DrawShieldLayer(handle, chain, transform, tex, layerColor, outerOffset, innerOffset, verts);
            verts.Clear();
        }
    }

    private void DrawShieldLayer(
        DrawingHandleWorld handle,
        ChainShape chain,
        Transform transform,
        Texture tex,
        Color color,
        float outerOffset,
        float innerOffset,
        List<DrawVertexUV2D> verts)
    {
        // The vertices of this fixture are defined relative to local position,
        // so we'll have to add them to this and then use the matrix to put them back in world position.
        // If "Transforms" ever get deprecated go ahead and check how DebugPHysicsSystem is drawing chains in this hellworld future

        // Mono Update: Just use transform.Position for world position already for corners

        for (int i = 1; i < chain.Count; i++)
        {
            var leftBaseVertex = VertexToWorldPos(chain.Vertices[i - 1], transform);
            var rightBaseVertex = VertexToWorldPos(chain.Vertices[i], transform);

            var leftVertex = OffsetVertex(leftBaseVertex, transform, outerOffset);
            var rightVertex = OffsetVertex(rightBaseVertex, transform, outerOffset);
            var leftCorner = OffsetVertex(leftBaseVertex, transform, innerOffset);
            var rightCorner = OffsetVertex(rightBaseVertex, transform, innerOffset);

            verts.Add(new DrawVertexUV2D(leftVertex, new Vector2(0, 1)));
            verts.Add(new DrawVertexUV2D(rightVertex, new Vector2(1, 1)));
            verts.Add(new DrawVertexUV2D(leftCorner, Vector2.Zero));

            verts.Add(new DrawVertexUV2D(rightVertex, new Vector2(1, 1)));
            verts.Add(new DrawVertexUV2D(leftCorner, Vector2.Zero));
            verts.Add(new DrawVertexUV2D(rightCorner, new Vector2(1, 0)));
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, texture: tex, CollectionsMarshal.AsSpan(verts), color);
    }

    private static Color GetLayerColor(Color color, int layer, int layerCount)
    {
        if (layerCount == 1)
            return color;

        var progress = (float)layer / Math.Max(1, layerCount - 1);
        var alpha = 0.85f - progress * 0.35f;
        return color.WithAlpha(color.A * alpha);
    }

    private static Vector2 VertexToWorldPos(Vector2 vertexPos, Transform transform)
    {
        return Transform.Mul(transform, vertexPos);
    }

    private static Vector2 OffsetVertex(Vector2 vertexPos, Transform transform, float offset)
    {
        if (offset <= 0f)
            return vertexPos;

        var cornerPos = Vector2.Subtract(vertexPos, transform.Position);
        if (cornerPos.LengthSquared() <= float.Epsilon)
            return vertexPos;

        cornerPos = Vector2.Normalize(cornerPos) * offset;
        return Vector2.Subtract(vertexPos, cornerPos);
    }
    // Exodus-end layered ship shield visuals
}
