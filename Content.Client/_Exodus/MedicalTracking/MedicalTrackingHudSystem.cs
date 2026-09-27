using System.Numerics;
using Content.Shared._Exodus.MedicalTracking;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.MedicalTracking;

/// <summary>Decorates visible health icons without a separate entity scan or animation updates.</summary>
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

    /// <summary>Called only after the medical status icon passed the HUD's visibility checks.</summary>
    public void DrawBorder(EntityUid body, DrawingHandleWorld handle, Vector2 position, Vector2 iconSize)
    {
        if (!_hudQuery.TryComp(body, out var hud) || !_prototype.TryIndex(hud.Border, out var border))
            return;

        var texture = _sprite.GetFrame(border.Icon, _timing.RealTime);
        var offset = (iconSize - new Vector2(texture.Width, texture.Height)) / (2f * EyeManager.PixelsPerMeter);
        handle.UseShader(_unshaded);
        handle.DrawTexture(texture, position + offset);
    }
}
