using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Mining.AutoMining;

/// <summary>One animated ribbon for both the world and mass scanners, with no per-frame heap allocations.</summary>
public sealed class BulkAutoMiningBeamRenderer
{
    public const float WorldWidth = 1f;
    public static readonly ProtoId<ShaderPrototype> MiningShader = "ExodusBulkMiningBeam";
    public static readonly ProtoId<ShaderPrototype> LinkShader = "ExodusBulkMiningLinkBeam";
    private readonly ShaderInstance _shader;
    private readonly Texture _texture;
    // Reuse a managed buffer: stackalloc is rejected by the client sandbox's IL verifier.
    private readonly DrawVertexUV2D[] _vertices = new DrawVertexUV2D[4];

    public BulkAutoMiningBeamRenderer(IPrototypeManager prototypes) : this(prototypes, MiningShader)
    {
    }

    public BulkAutoMiningBeamRenderer(IPrototypeManager prototypes, ProtoId<ShaderPrototype> shader)
    {
        _shader = prototypes.Index(shader).Instance();
        _texture = Texture.White;
    }

    public void Draw(DrawingHandleBase handle, Vector2 origin, Vector2 target, float width, float waveDistance)
    {
        var delta = target - origin;
        var length = delta.Length();
        if (length < 0.01f)
            return;

        var normal = new Vector2(-delta.Y, delta.X) * (width * 0.5f / length);
        _vertices[0] = new DrawVertexUV2D(origin - normal, Vector2.Zero);
        _vertices[1] = new DrawVertexUV2D(origin + normal, Vector2.UnitY);
        _vertices[2] = new DrawVertexUV2D(target - normal, new Vector2(waveDistance, 0));
        _vertices[3] = new DrawVertexUV2D(target + normal, new Vector2(waveDistance, 1));
        handle.UseShader(_shader);
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, _texture, _vertices);
        handle.UseShader(null);
    }
}
