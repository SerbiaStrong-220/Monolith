using Content.Client._Exodus.StationAi; // Exodus
using Content.Shared._Exodus.Examine;
using Content.Client.Silicons.StationAi;
using Content.Client.Viewport;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared.Verbs;
using Content.Client.UserInterface.Controls;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Virology.Intelligent;

public sealed partial class RotIntelligentSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    private StationAiOverlay? _mask;
    private RotBuildOverlay? _preview;
    private ScalingViewport? _buildViewport;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RotIntelligentComponent, LocalPlayerAttachedEvent>(OnAttached);
        SubscribeLocalEvent<RotIntelligentComponent, LocalPlayerDetachedEvent>(OnDetached);
        SubscribeLocalEvent<RotIntelligentComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<RotIntelligentComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<RotIntelligentComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<RotIntelligentComponent, MenuVisibilityEvent>(OnMenu);
        SubscribeLocalEvent<RotIntelligentComponent, RemoteExamineEvent>(OnExamine);
        SubscribeNetworkEvent<RotVisionEvent>(OnVision);
        _ui.OnScreenChanged += OnScreenChanged;
    }

    private void OnAttached(Entity<RotIntelligentComponent> ent, ref LocalPlayerAttachedEvent args) => Attach(ent);

    private void OnDetached(Entity<RotIntelligentComponent> ent, ref LocalPlayerDetachedEvent args) => RemoveOverlays();

    private void OnScreenChanged((UIScreen? Old, UIScreen? New) args)
    {
        BindBuildWheel();
    }

    private void OnState(Entity<RotIntelligentComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!ent.Comp.Alive && TryComp<CameraViewMaskComponent>(ent, out var view))
        {
            view.Frame = null;
            view.Tiles.Clear();
        }
        if (_player.LocalEntity != ent.Owner)
            return;
        if (!ent.Comp.Alive || !ent.Comp.Rooted)
        {
            RemoveOverlays();
            RemCompDeferred<CameraViewMaskComponent>(ent);
        }
        else
            Attach(ent);
    }

    private void OnInit(Entity<RotIntelligentComponent> ent, ref ComponentInit args)
    {
        if (_player.LocalEntity == ent.Owner)
            Attach(ent);
    }

    private void OnShutdown(Entity<RotIntelligentComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity == ent.Owner)
            RemoveOverlays();
    }

    private void Attach(EntityUid core)
    {
        if (!TryComp<RotIntelligentComponent>(core, out var brain) || !brain.Alive || !brain.Rooted)
            return;
        EnsureComp<CameraViewMaskComponent>(core);
        if (_mask != null)
            return;
        _mask = new StationAiOverlay();
        _preview = new RotBuildOverlay();
        _overlays.AddOverlay(_mask);
        _overlays.AddOverlay(_preview);
        BindBuildWheel();
        RaiseNetworkEvent(new RotVisionRequestEvent());
    }

    private void BindBuildWheel()
    {
        var viewport = _ui.ActiveScreen?.GetWidget<MainViewport>()?.Viewport;
        if (_buildViewport == viewport)
            return;

        UnbindBuildWheel();
        if (viewport == null)
            return;

        _buildViewport = viewport;
        _buildViewport.WheelScrolled += OnBuildWheel;
    }

    private void UnbindBuildWheel()
    {
        if (_buildViewport == null)
            return;

        _buildViewport.WheelScrolled -= OnBuildWheel;
        _buildViewport = null;
    }

    private void OnBuildWheel(GUIMouseWheelEventArgs args)
    {
        if (args.Handled || MathF.Abs(args.Delta.Y) < float.Epsilon
            || _player.LocalEntity is not { } core
            || !TryComp<RotIntelligentComponent>(core, out var colony)
            || !colony.Alive || !colony.Rooted || colony.ChangingForm || colony.BuildAction == null
            || _ui.GetUIController<Content.Client.UserInterface.Systems.Actions.ActionUIController>().SelectingTargetFor
                != colony.BuildAction)
            return;

        RaiseNetworkEvent(new RotRotateBuildingMessage((sbyte)Math.Sign(args.Delta.Y)));
        args.Handle();
    }

    private void RemoveOverlays()
    {
        if (_mask != null)
            _overlays.RemoveOverlay(_mask);
        if (_preview != null)
            _overlays.RemoveOverlay(_preview);
        _mask = null;
        _preview = null;
        UnbindBuildWheel();
    }

    private void OnVision(RotVisionEvent args)
    {
        var core = GetEntity(args.Core);
        if (_player.LocalEntity != core || !TryComp<RotIntelligentComponent>(core, out var brain) || !brain.Alive || !brain.Rooted)
            return;
        var view = EnsureComp<CameraViewMaskComponent>(core);
        view.Frame = GetEntity(args.Grid);
        view.Tiles.Clear();
        view.Tiles.UnionWith(args.Tiles);
    }

    public bool CanSee(EntityUid core, MapCoordinates point)
    {
        if (!TryComp<CameraViewMaskComponent>(core, out var view) || view.Frame is not { } frame
            || Deleted(frame) || Transform(frame).MapID != point.MapId)
            return false;
        var local = _transform.ToCoordinates(frame, point).Position;
        var size = TryComp<MapGridComponent>(frame, out var grid) ? grid.TileSize : 1;
        return view.Tiles.Contains(new Vector2i((int)MathF.Floor(local.X / size), (int)MathF.Floor(local.Y / size)));
    }

    private void OnMenu(Entity<RotIntelligentComponent> ent, ref MenuVisibilityEvent args)
    {
        if (!ent.Comp.Rooted)
            return;
        args.Cancelled |= !CanSee(ent, args.TargetPos);
        args.Visibility |= MenuVisibility.NoFov;
    }

    private void OnExamine(Entity<RotIntelligentComponent> ent, ref RemoteExamineEvent args)
    {
        if (!ent.Comp.Rooted)
            return;
        args.Handled = true;
        args.Allowed = CanSee(ent, args.Target)
            && (args.Examined is not { } target || !_containers.IsEntityInContainer(target));
    }

    public override void Shutdown()
    {
        _ui.OnScreenChanged -= OnScreenChanged;
        RemoveOverlays();
        base.Shutdown();
    }
}
