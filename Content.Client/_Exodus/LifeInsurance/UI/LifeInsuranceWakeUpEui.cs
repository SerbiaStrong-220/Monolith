using Content.Client.Eui;
using Content.Shared._Exodus.LifeInsurance;
using JetBrains.Annotations;
using Robust.Client.Graphics;

namespace Content.Client._Exodus.LifeInsurance.UI;

[UsedImplicitly]
public sealed class LifeInsuranceWakeUpEui : BaseEui
{
    [Dependency] private IClyde _clyde = default!;

    private readonly LifeInsuranceWakeUpWindow _window;

    public LifeInsuranceWakeUpEui()
    {
        _window = new LifeInsuranceWakeUpWindow();

        _window.OkButton.OnPressed += _ => _window.Close();

        _window.OnClose += () => SendMessage(new LifeInsuranceWakeUpClosedMessage());
    }

    public override void Opened()
    {
        _clyde.RequestWindowAttention();
        _window.OpenCentered();
    }

    public override void Closed()
    {
        _window.Close();
    }
}
