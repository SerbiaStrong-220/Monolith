using System.Numerics;
using Content.Shared._Exodus.Virology.Intelligent;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Virology.Intelligent;

public sealed class RotIntelligentWindow : DefaultWindow
{
    private readonly Label _resources = new();
    private readonly Label _selection = new();
    private readonly RichTextLabel _feedback = new();
    private readonly Label _emptyCameras = new() { Text = Loc.GetString("rot-intelligent-no-cameras") };
    private readonly Label _emptyProjects = new() { Text = Loc.GetString("rot-intelligent-no-projects") };
    private readonly BoxContainer _buildings = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _cameras = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly BoxContainer _projects = new() { Orientation = BoxContainer.LayoutOrientation.Vertical };
    private readonly Dictionary<ProtoId<RotBuildingPrototype>, (Button Button, float Cost)> _choices = [];
    private readonly Dictionary<NetEntity, Button> _cameraRows = [];
    private readonly Dictionary<NetEntity, (BoxContainer Row, ProgressBar Progress)> _projectRows = [];
    private readonly List<NetEntity> _removed = [];
    private readonly HashSet<NetEntity> _present = [];
    public event Action<ProtoId<RotBuildingPrototype>>? Selected;
    public event Action<BoundUserInterfaceMessage>? Message;

    public RotIntelligentWindow()
    {
        Title = Loc.GetString("rot-intelligent-menu");
        MinSize = new Vector2(370, 420);
        SetSize = new Vector2(420, 620);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        Contents.AddChild(root);
        root.AddChild(_resources);
        root.AddChild(_selection);
        var controls = new GridContainer { Columns = 2, HSeparationOverride = 4, VSeparationOverride = 4 };
        foreach (var command in Enum.GetValues<RotColonyCommand>())
        {
            var button = new Button { Text = Loc.GetString($"rot-intelligent-command-{command.ToString().ToLowerInvariant()}"), HorizontalExpand = true };
            button.OnPressed += _ => Message?.Invoke(new RotColonyCommandMessage(command));
            controls.AddChild(button);
        }
        root.AddChild(controls);
        root.AddChild(_feedback);
        _cameras.AddChild(_emptyCameras);
        _projects.AddChild(_emptyProjects);
        var tabs = new TabContainer { VerticalExpand = true };
        AddTab(tabs, _buildings, "rot-intelligent-buildings");
        AddTab(tabs, _cameras, "rot-intelligent-cameras");
        AddTab(tabs, _projects, "rot-intelligent-projects");
        root.AddChild(tabs);
        var hint = new RichTextLabel();
        hint.SetMessage(Loc.GetString("rot-intelligent-placement-hint"));
        root.AddChild(hint);
    }

    private static void AddTab(TabContainer tabs, BoxContainer content, string title)
    {
        var scroll = new ScrollContainer { HScrollEnabled = false };
        scroll.AddChild(content);
        tabs.AddChild(scroll);
        TabContainer.SetTabTitle(scroll, Loc.GetString(title));
    }

    public void Configure(RotIntelligentComponent core, IPrototypeManager prototypes)
    {
        if (_choices.Count != 0)
            return;
        foreach (var id in core.Buildings)
        {
            var recipe = prototypes.Index(id);
            var button = new Button
            {
                Text = Loc.GetString("rot-intelligent-recipe", ("name", Loc.GetString(recipe.Name)), ("cost", recipe.Cost),
                    ("width", recipe.Size.X), ("height", recipe.Size.Y)),
                ToolTip = Loc.GetString(recipe.Description),
                HorizontalExpand = true,
                MinHeight = 36,
            };
            button.OnPressed += _ => Selected?.Invoke(id);
            _choices.Add(id, (button, recipe.Cost));
            _buildings.AddChild(button);
        }
    }

    public void RefreshSelection(RotIntelligentComponent core, IPrototypeManager prototypes)
    {
        _selection.Text = Loc.GetString("rot-intelligent-selection", ("name", Loc.GetString(prototypes.Index(core.SelectedBuilding).Name)),
            ("angle", core.Rotation * 90));
        foreach (var (id, choice) in _choices)
        {
            choice.Button.Disabled = !core.Alive || !core.Rooted || core.ChangingForm || core.Biomass < choice.Cost;
            choice.Button.Modulate = id == core.SelectedBuilding ? Color.LightGreen : Color.White;
        }
    }

    public void UpdateState(RotIntelligentUiState state)
    {
        _resources.Text = Loc.GetString("rot-intelligent-resources", ("amount", MathF.Floor(state.Biomass)),
            ("capacity", state.Capacity), ("income", state.Income));
        _feedback.SetMessage(state.Feedback ?? Loc.GetString("rot-intelligent-ready"));
        _emptyCameras.Visible = state.Cameras.Count == 0;
        _emptyProjects.Visible = state.Jobs.Count == 0;
        _removed.Clear();
        _present.Clear();
        foreach (var camera in state.Cameras)
            _present.Add(camera.Entity);
        foreach (var uid in _cameraRows.Keys)
        {
            if (!_present.Contains(uid))
                _removed.Add(uid);
        }
        foreach (var uid in _removed)
        {
            _cameras.RemoveChild(_cameraRows[uid]);
            _cameraRows.Remove(uid);
        }
        foreach (var camera in state.Cameras)
        {
            if (!_cameraRows.TryGetValue(camera.Entity, out var row))
            {
                var button = new Button { HorizontalExpand = true, MinHeight = 32 };
                button.OnPressed += _ => Message?.Invoke(new RotJumpMessage(camera.Entity));
                _cameras.AddChild(button);
                row = button;
                _cameraRows.Add(camera.Entity, row);
            }
            row.Text = camera.Alert ? Loc.GetString("rot-intelligent-camera-alert", ("name", camera.Name)) : camera.Name;
            row.Modulate = camera.Alert ? Color.OrangeRed : Color.White;
        }
        _removed.Clear();
        _present.Clear();
        foreach (var job in state.Jobs)
            _present.Add(job.Entity);
        foreach (var uid in _projectRows.Keys)
        {
            if (!_present.Contains(uid))
                _removed.Add(uid);
        }
        foreach (var uid in _removed)
        {
            _projects.RemoveChild(_projectRows[uid].Row);
            _projectRows.Remove(uid);
        }
        foreach (var job in state.Jobs)
        {
            if (!_projectRows.TryGetValue(job.Entity, out var row))
            {
                var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
                box.AddChild(new Label { Text = job.Name });
                var progress = new ProgressBar { MinValue = 0, MaxValue = 1, MinHeight = 18 };
                box.AddChild(progress);
                var cancel = new Button { Text = Loc.GetString("rot-intelligent-cancel") };
                cancel.OnPressed += _ => Message?.Invoke(new RotCancelProjectMessage(job.Entity));
                box.AddChild(cancel);
                _projects.AddChild(box);
                row = (box, progress);
                _projectRows.Add(job.Entity, row);
            }
            row.Progress.Value = job.Progress;
        }
    }
}
