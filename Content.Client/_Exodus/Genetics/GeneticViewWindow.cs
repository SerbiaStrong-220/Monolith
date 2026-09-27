using System.Numerics;
using Content.Client.Eye;
using Content.Client.UserInterface.Controls;
using Content.Client.Viewport;
using Content.Shared._Exodus.Genetics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticViewWindow : FancyWindow
{
    [Dependency] private readonly IEntityManager _entities = default!;
    public event Action<NetEntity?>? TargetSelected;
    public event Action? RefreshRequested;
    private readonly BoxContainer _targets = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly ScalingViewport _viewport = new() { MinSize = new Vector2(280, 280), HorizontalExpand = true, VerticalExpand = true };
    private readonly FixedEye _emptyEye = new();
    private readonly EyeLerpingSystem _lerping;
    private NetEntity? _eye;
    private EntityUid? _currentEye;

    public GeneticViewWindow()
    {
        IoCManager.InjectDependencies(this);
        _lerping = _entities.System<EyeLerpingSystem>();
        Title = Loc.GetString("genetics-view-title");
        MinSize = new Vector2(520, 520);
        SetSize = new Vector2(600, 600);
        _viewport.Eye = _emptyEye;
        _viewport.ViewportSize = new Vector2i(500, 500);
        _viewport.MouseFilter = MouseFilterMode.Ignore;
        var root = new BoxContainer();
        var sidebar = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SetWidth = 220 };
        var refresh = new Button { Text = Loc.GetString("genetics-view-refresh") };
        refresh.OnPressed += _ => RefreshRequested?.Invoke();
        var stop = new Button { Text = Loc.GetString("genetics-view-stop") };
        stop.OnPressed += _ => TargetSelected?.Invoke(null);
        sidebar.AddChild(refresh);
        sidebar.AddChild(stop);
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        scroll.AddChild(_targets);
        sidebar.AddChild(scroll);
        root.AddChild(sidebar);
        root.AddChild(_viewport);
        ContentsContainer.AddChild(root);
    }

    public void UpdateState(GeneticViewState state)
    {
        _eye = state.Eye;
        _targets.DisposeAllChildren();
        foreach (var (target, name) in state.Targets)
        {
            var button = new Button { Text = name, ClipText = true, ToolTip = name };
            button.OnPressed += _ => TargetSelected?.Invoke(target);
            _targets.AddChild(button);
        }
        if (state.Targets.Count == 0)
        {
            var empty = new RichTextLabel();
            empty.SetMessage(Loc.GetString("genetics-view-empty"));
            _targets.AddChild(empty);
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        // The UI state can arrive before the remote eye enters PVS.
        if (!_entities.TryGetEntity(_eye, out var uid) || !_entities.TryGetComponent<EyeComponent>(uid, out var eye))
        {
            ClearEye();
            return;
        }
        if (_currentEye != uid)
        {
            ClearEye();
            _currentEye = uid;
            _lerping.AddEye(uid.Value, eye);
        }
        _viewport.Eye = eye.Eye ?? _emptyEye;
    }

    private void ClearEye()
    {
        if (_currentEye is { } eye && _entities.EntityExists(eye))
            _lerping.RemoveEye(eye);
        _currentEye = null;
        _viewport.Eye = _emptyEye;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        ClearEye();
    }
}
