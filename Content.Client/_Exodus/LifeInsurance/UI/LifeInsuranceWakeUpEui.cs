using Content.Client.Eui;
using Content.Shared._Exodus.LifeInsurance;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._Exodus.LifeInsurance.UI;

[UsedImplicitly]
public sealed class LifeInsuranceWakeUpEui : BaseEui
{
    private readonly LifeInsuranceWakeUpWindow _window;

    public LifeInsuranceWakeUpEui()
    {
        _window = new LifeInsuranceWakeUpWindow();

        _window.OkButton.OnPressed += _ =>
        {
            SendMessage(new LifeInsuranceWakeUpClosedMessage());
            _window.Close();
        };

        _window.OnClose += () => SendMessage(new LifeInsuranceWakeUpClosedMessage());
    }

    public override void Opened()
    {
        IoCManager.Resolve<IClyde>().RequestWindowAttention();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }
}
