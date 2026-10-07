using System.Numerics;
using System.Runtime.InteropServices;
using Content.Shared._Crescent.ShipShields;
using Robust.Client.Graphics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.ShipShields;

/// <summary>
/// Draws the same animated shield contour in the world and on mass scanners.
/// </summary>
public sealed class ShipShieldRippleRenderer : IDisposable
{
    private readonly IPrototypeManager _prototypes;
    private readonly Dictionary<(ProtoId<ShaderPrototype> Shader, float Speed), ShaderInstance> _shaders = new();
    private readonly List<DrawVertexUV2D> _vertices = new(128);

    public ShipShieldRippleRenderer(IPrototypeManager prototypes)
    {
        _prototypes = prototypes;
    }

    /// <param name="localToView">Transforms fixture vertices into world or control coordinates.</param>
    /// <param name="minimumWidth">Minimum visible band width, in fixture coordinates.</param>
    public void Draw(
        DrawingHandleBase handle,
        ChainShape chain,
        Matrix3x2 localToView,
        ShipShieldVisualsComponent visuals,
        float minimumWidth = 0f)
    {
        if (visuals.RippleWidth <= 0f || chain.Vertices.Length < 2)
            return;

        // Draw commands are deferred. Share immutable instances by shader and speed so shields cannot
        // overwrite each other's uniforms or allocate boxed parameter values every frame.
        ProtoId<ShaderPrototype> shaderId = visuals.RippleShader;
        var shaderKey = (shaderId, visuals.RippleSpeed);
        if (!_shaders.TryGetValue(shaderKey, out var shader))
        {
            shader = _prototypes.Index(shaderId).InstanceUnique();
            shader.SetParameter("waveSpeed", visuals.RippleSpeed);
            shader.MakeImmutable();
            _shaders.Add(shaderKey, shader);
        }

        var width = MathF.Max(visuals.RippleWidth, minimumWidth);
        var segments = chain.Vertices.Length - 1;
        _vertices.Clear();

        for (var i = 1; i <= segments; i++)
        {
            var left = chain.Vertices[i - 1];
            var right = chain.Vertices[i];
            var leftOuter = Vector2.Transform(left, localToView);
            var rightOuter = Vector2.Transform(right, localToView);
            var leftInner = Vector2.Transform(InsetVertex(left, width), localToView);
            var rightInner = Vector2.Transform(InsetVertex(right, width), localToView);

            // Continuous UVs keep the traveling waves seamless around the whole contour.
            var leftU = (float) (i - 1) / segments;
            var rightU = (float) i / segments;
            _vertices.Add(new DrawVertexUV2D(leftOuter, new Vector2(leftU, 1)));
            _vertices.Add(new DrawVertexUV2D(rightOuter, new Vector2(rightU, 1)));
            _vertices.Add(new DrawVertexUV2D(leftInner, new Vector2(leftU, 0)));
            _vertices.Add(new DrawVertexUV2D(rightOuter, new Vector2(rightU, 1)));
            _vertices.Add(new DrawVertexUV2D(leftInner, new Vector2(leftU, 0)));
            _vertices.Add(new DrawVertexUV2D(rightInner, new Vector2(rightU, 0)));
        }

        var previousShader = handle.GetShader();
        handle.UseShader(shader);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
            CollectionsMarshal.AsSpan(_vertices), visuals.ShieldColor);
        handle.UseShader(previousShader);
    }

    private static Vector2 InsetVertex(Vector2 vertex, float width)
    {
        var radius = vertex.Length();
        if (radius <= float.Epsilon)
            return vertex;

        // At distant scanner zooms the minimum pixel width must not cross the shield's center.
        return vertex * (1f - MathF.Min(width / radius, 0.75f));
    }

    public void Dispose()
    {
        foreach (var shader in _shaders.Values)
            shader.Dispose();

        _shaders.Clear();
    }
}
