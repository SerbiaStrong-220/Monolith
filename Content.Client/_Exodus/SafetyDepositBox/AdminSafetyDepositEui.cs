using Content.Client.Eui;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared.Eui;
using JetBrains.Annotations;

namespace Content.Client._Exodus.SafetyDepositBox;

[UsedImplicitly]
public sealed class AdminSafetyDepositEui : BaseEui
{
    private readonly AdminSafetyDepositWindow _window = new();

    public AdminSafetyDepositEui()
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
    }

    public override void HandleState(EuiStateBase state)
    {
        if (state is AdminSafetyDepositEuiState data)
            _window.UpdateState(data);
    }

    private void OnClose() => SendMessage(new CloseEuiMessage());
}
