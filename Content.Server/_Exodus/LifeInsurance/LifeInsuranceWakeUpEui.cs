using Content.Server.EUI;
using Content.Shared._Exodus.LifeInsurance;
using Content.Shared.Eui;

namespace Content.Server._Exodus.LifeInsurance;

/// <summary>
/// Server side of the narrative "you wake up in the incubator" window shown to a freshly cloned player.
/// </summary>
public sealed class LifeInsuranceWakeUpEui : BaseEui
{
    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is LifeInsuranceWakeUpClosedMessage)
            Close();
    }
}
