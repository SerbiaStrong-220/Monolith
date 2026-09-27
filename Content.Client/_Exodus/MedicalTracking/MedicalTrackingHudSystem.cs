using System.Numerics;
using Content.Shared._Exodus.MedicalTracking;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.MedicalTracking;

/// <summary>Replaces health icon frames without a separate entity scan or animation updates.</summary>
public sealed class MedicalTrackingHudSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private EntityQuery<MedicalTrackingHudComponent> _hudQuery;
    private ShaderInstance _unshaded = default!;

    public override void Initialize()
    {
        base.Initialize();
        _hudQuery = GetEntityQuery<MedicalTrackingHudComponent>();
        _unshaded = _prototype.Index<ShaderPrototype>("unshaded").Instance();
    }

    /// <summary>
    /// Draws a health icon with its one-pixel frame replaced by the implant's animated border.
    /// Returns false to keep the original icon when no compatible service border is available.
    /// Called only after the medical status icon passed the HUD's visibility checks.
    /// </summary>
    public bool TryDrawIcon(EntityUid body, DrawingHandleWorld handle, Vector2 position, Texture icon)
    {
        if (!_hudQuery.TryComp(body, out var hud) || !_prototype.TryIndex(hud.Border, out var border))
            return false;

        var texture = _sprite.GetFrame(border.Icon, _timing.RealTime);
        if (texture.Size != icon.Size || icon.Width <= 2 || icon.Height <= 2)
            return false;

        // Keep the current health frame and lighting, but leave its rim transparent for the fading tails.
        var interior = new UIBox2(1, 1, icon.Width - 1, icon.Height - 1);
        var quad = Box2.FromDimensions(position + Vector2.One / EyeManager.PixelsPerMeter,
            interior.Size / EyeManager.PixelsPerMeter);
        handle.DrawTextureRectRegion(icon, quad, subRegion: interior);
        handle.UseShader(_unshaded);
        handle.DrawTexture(texture, position);
        return true;
    }
}
