// (c) Space Exodus Team - EXDS-RL with CLA

using Content.Client.SS220.UserInterface.EPA;
using Content.Shared.SS220.EPA;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.Client.SS220.EPA;

public sealed partial class EPAAuthState : State
{
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IClientEPAManager _epa = default!;

    private EPAAuthPanel? _panel;

    private const float Timer = 10f;
    private float _timer = 0;

    protected override void Startup()
    {
        _panel = new EPAAuthPanel();
        _ui.StateRoot.AddChild(_panel);
    }

    public override void FrameUpdate(FrameEventArgs e)
    {
        base.FrameUpdate(e);

        _timer += e.DeltaSeconds;
        if (_timer < Timer)
            return;
        _timer = 0;

        _epa.CheckAuthState();
    }

    protected override void Shutdown()
    {
        if (_panel != null)
            _ui.StateRoot.RemoveChild(_panel);
    }
}
