using System.Numerics;
using Content.Client._Exodus.Decals; // Exodus
using Content.Shared.Decals; // Exodus
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client.Decals.Overlays;

public sealed partial class DecalPlacementOverlay : Overlay
{
    [Dependency] private IEyeManager _eyeManager = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IMapManager _mapManager = default!;
    [Dependency] private IPrototypeManager _protoMan = default!; // Exodus
    [Dependency] private IDecalPlacementManager _tool = default!; // Exodus
    private readonly DecalPlacementSystem _placement;
    private readonly SharedTransformSystem _transform;
    private readonly SpriteSystem _sprite;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities;

    public DecalPlacementOverlay(DecalPlacementSystem placement, SharedTransformSystem transform, SpriteSystem sprite)
    {
        IoCManager.InjectDependencies(this);
        _placement = placement;
        _transform = transform;
        _sprite = sprite;
        ZIndex = 1000;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Exodus-Start
        if (_tool.EyedropperActive)
        {
            DrawEyedropper(args);
            return;
        }

        if (_tool.Stamping)
        {
            DrawStamp(args);
            return;
        }
        // Exodus-End

        var (decal, snap, rotation, color) = _placement.GetActiveDecal();

        if (decal == null)
            return;

        var mouseScreenPos = _inputManager.MouseScreenPosition;
        var mousePos = _eyeManager.PixelToMap(mouseScreenPos);

        if (mousePos.MapId != args.MapId)
            return;

        // No map support for decals
        if (!_mapManager.TryFindGridAt(mousePos, out var gridUid, out var grid))
        {
            return;
        }

        var worldMatrix = _transform.GetWorldMatrix(gridUid);
        var invMatrix = _transform.GetInvWorldMatrix(gridUid);

        var handle = args.WorldHandle;
        handle.SetTransform(worldMatrix);

        var localPos = Vector2.Transform(mousePos.Position, invMatrix);

        if (snap)
        {
            localPos = localPos.Floored() + grid.TileSizeHalfVector;
        }

        // Nothing uses snap cardinals so probably don't need preview?
        var aabb = Box2.UnitCentered.Translated(localPos);
        var box = new Box2Rotated(aabb, rotation, localPos);

        handle.DrawTextureRect(_sprite.Frame0(decal.Sprite), box, color);
        handle.SetTransform(Matrix3x2.Identity);
    }

    // Exodus-Start preview of the copied decal stack.
    private void DrawStamp(in OverlayDrawArgs args)
    {
        var mouseScreenPos = _inputManager.MouseScreenPosition;
        var mousePos = _eyeManager.PixelToMap(mouseScreenPos);

        if (mousePos.MapId != args.MapId)
            return;

        if (!_mapManager.TryFindGridAt(mousePos, out var gridUid, out _))
            return;

        var handle = args.WorldHandle;
        handle.SetTransform(_transform.GetWorldMatrix(gridUid));

        var localPos = Vector2.Transform(mousePos.Position, _transform.GetInvWorldMatrix(gridUid));
        var origin = localPos.Floored();

        foreach (var (offset, decal) in _tool.Stamp)
        {
            if (!_protoMan.TryIndex<DecalPrototype>(decal.Id, out var proto))
                continue;

            var texture = _sprite.Frame0(proto.Sprite);
            var pos = origin + offset;
            var color = decal.Color ?? Color.White;

            if (decal.Angle == Angle.Zero)
                handle.DrawTexture(texture, pos, color);
            else
                handle.DrawTexture(texture, pos, decal.Angle, color);
        }

        handle.SetTransform(Matrix3x2.Identity);
    }

    // crosshair of eyedropper tool.
    private void DrawEyedropper(in OverlayDrawArgs args)
    {
        var mouseScreenPos = _inputManager.MouseScreenPosition;
        var mousePos = _eyeManager.PixelToMap(mouseScreenPos);

        if (mousePos.MapId != args.MapId)
            return;

        // The handle is already in world space here so draw directly.
        var handle = args.WorldHandle;
        var pos = mousePos.Position;
        const float arm = 0.35f;
        const float gap = 0.08f;

        // Dark outline first so crosshair stays visible over any decal.
        DrawCross(handle, pos, arm, gap, 0.04f, Color.Black.WithAlpha(0.6f));
        DrawCross(handle, pos, arm, gap, 0f, Color.White);
        handle.DrawCircle(pos, gap, Color.White, false);
    }

    private static void DrawCross(DrawingHandleWorld handle, Vector2 pos, float arm, float gap, float pad, Color color)
    {
        handle.DrawLine(pos + new Vector2(-arm - pad, 0f), pos + new Vector2(-gap + pad, 0f), color);
        handle.DrawLine(pos + new Vector2(gap - pad, 0f), pos + new Vector2(arm + pad, 0f), color);
        handle.DrawLine(pos + new Vector2(0f, -arm - pad), pos + new Vector2(0f, -gap + pad), color);
        handle.DrawLine(pos + new Vector2(0f, gap - pad), pos + new Vector2(0f, arm + pad), color);
    }
    // Exodus-End
}
