using Content.Client.Eui;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._Exodus.Genetics;

[UsedImplicitly]
public sealed class GeneticsAdminEui : BaseEui
{
    private readonly GeneticsAdminWindow _window = new();

    public GeneticsAdminEui()
    {
        _window.BlockChanged += message => SendMessage(message);
        _window.RefreshRequested += () => SendMessage(new GeneticsAdminRefreshMessage());
        _window.OnClose += OnClose;
    }

    public override void Opened() => _window.OpenCentered();

    public override void Closed()
    {
        base.Closed();
        _window.OnClose -= OnClose;
        _window.Close();
    }

    private void OnClose()
    {
        SendMessage(new CloseEuiMessage());
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is GeneticsAdminState data)
            _window.UpdateState(data);
    }
}
