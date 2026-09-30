// Exodus: reset shaders supplied by old base layers before rebuilding a humanoid's appearance.
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;

namespace Content.Client.Humanoid;

public sealed partial class HumanoidAppearanceSystem
{
    private void ResetBaseLayerShaders(Entity<HumanoidAppearanceComponent, SpriteComponent> ent)
    {
        var (uid, appearance, sprite) = ent;
        foreach (var (key, prototype) in appearance.BaseLayers)
        {
            if (prototype.Shader == null ||
                !_spriteSystem.LayerMapTryGet((uid, sprite), key, out var index, false) ||
                !_spriteSystem.TryGetLayer((uid, sprite), index, out var layer, false) ||
                layer.ShaderPrototype?.Id != prototype.Shader)
                continue;

            sprite.LayerSetShader(index, null, null);
        }
    }
}
