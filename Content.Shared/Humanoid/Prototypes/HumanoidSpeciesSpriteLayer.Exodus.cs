// Exodus: allow base species sprites to use shaders, like humanoid markings.
namespace Content.Shared.Humanoid.Prototypes;

public sealed partial class HumanoidSpeciesSpriteLayer
{
    /// <summary>Optional layer shader ID. ShaderPrototype is client-only, so shared data stores its name.</summary>
    [DataField]
    public string? Shader;
}
