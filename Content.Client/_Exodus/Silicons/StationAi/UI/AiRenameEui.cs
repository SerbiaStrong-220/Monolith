using Content.Client.Eui;
using Content.Shared._Exodus.Silicons.StationAi;
using Content.Shared.Eui;

namespace Content.Client._Exodus.Silicons.StationAi.UI;

public sealed class AiRenameEui : BaseEui
{
    private AiRenameWindow? _window;

    public override void Opened()
    {
        base.Opened();
        _window = new AiRenameWindow();
        _window.OnConfirmed += name => SendMessage(new AiRenameEuiMessage(name));
        _window.OnClose += () =>
        {
            if (_window != null && !_window.WasConfirmed)
                SendMessage(new AiRenameEuiMessage(string.Empty));
        };
        _window.OpenCentered();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is not AiRenameEuiState s || _window == null)
            return;

        _window.SetCurrentName(s.CurrentName);
    }

    public override void Closed()
    {
        base.Closed();
        _window?.Close();
        _window = null;
    }
}
