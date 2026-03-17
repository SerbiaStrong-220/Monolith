// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Client.Eui;
using Content.Shared._Exodus.Gimmicks.Pheromones.UI;

namespace Content.Client._Exodus.Gimmicks.Pheromones.UI;

public sealed partial class PheromonesAskEui : BaseEui
{
    private PheromonesAskWindow? _window = null;

    public override void Opened()
    {
        base.Opened();
        Close();

        _window = new PheromonesAskWindow();
        _window.OnPheromonesTextSent += (text) =>
        {
            SendMessage(new PheromonesAskEuiConfirmMessage(text));
        };
        _window.OpenCentered();
    }

    public void Close()
    {
        if (_window != null)
        {
            _window.Close();
            _window = null;
        }
    }

    public override void Closed()
    {
        base.Closed();
        Close();
    }
}
