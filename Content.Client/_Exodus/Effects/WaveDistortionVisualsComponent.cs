using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Effects;

/// <summary>Directional refraction across a projectile's footprint, optionally attenuated by DistanceFalloff.</summary>
[RegisterComponent]
public sealed partial class WaveDistortionVisualsComponent : Component
{
    [DataField(required: true)]
    public ProtoId<ShaderPrototype> Shader;

    /// <summary>Size of the rendered wave in world metres.</summary>
    [DataField]
    public Vector2 Size = Vector2.One;

    /// <summary>Maximum screen displacement at the default zoom, in pixels.</summary>
    [DataField]
    public float Intensity = 4f;

    /// <summary>Maximum opacity of the refracted background.</summary>
    [DataField]
    public float Opacity = 0.85f;

    /// <summary>Width of the deformation band in normalized half-tile coordinates.</summary>
    [DataField]
    public float FrontWidth = 0.4f;

    /// <summary>Curvature of the wave front in local coordinates.</summary>
    [DataField]
    public float Curvature = 0.6f;

    public ShaderInstance? Instance;
}
