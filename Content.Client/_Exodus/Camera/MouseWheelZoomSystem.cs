using Content.Client.Movement.Systems;
using Content.Client.UserInterface.Controls;
using Content.Client.Viewport;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;

namespace Content.Client._Exodus.Camera;

/// <summary>
/// Zooms the main world view when no other viewport action consumes the mouse wheel.
/// </summary>
public sealed partial class MouseWheelZoomSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private ContentEyeSystem _eye = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;

    public void HandleMouseWheel(ScalingViewport viewport, GUIMouseWheelEventArgs args)
    {
        if (args.Handled || args.Delta.Y == 0
            || !_config.GetCVar(EXCVars.MouseWheelZoomEnabled)
            || _ui.ActiveScreen?.GetWidget<MainViewport>()?.Viewport != viewport
            || _player.LocalEntity is not { } player
            || !TryComp<ContentEyeComponent>(player, out var content))
            return;

        var zoom = args.Delta.Y > 0
            ? content.TargetZoom / SharedContentEyeSystem.ZoomMod
            : content.TargetZoom * SharedContentEyeSystem.ZoomMod;

        _eye.RequestZoom(player, zoom, ignoreLimit: false, scalePvs: false, content: content);
        args.Handle();
    }
}
