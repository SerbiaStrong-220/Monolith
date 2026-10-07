using Content.Client.Eui;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._Exodus.Economy.Admin;

[UsedImplicitly]
public sealed class MarketAdminEui : BaseEui
{
    private readonly MarketAdminWindow _window = new();

    public MarketAdminEui()
    {
        _window.Send += SendMessage;
        _window.OnClose += OnClose;
    }

    public override void Opened() => _window.OpenCentered();

    public override void Closed()
    {
        base.Closed();
        _window.OnClose -= OnClose;
        _window.Send -= SendMessage;
        _window.Close();
        _window.Dispose();
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is MarketAdminState data)
            _window.UpdateState(data);
    }

    private void OnClose() => SendMessage(new CloseEuiMessage());
}
