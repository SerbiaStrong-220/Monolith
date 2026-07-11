using Content.Server.EUI;
using Content.Shared._Exodus.LifeInsurance;
using Content.Shared.Eui;

namespace Content.Server._Exodus.LifeInsurance;

public sealed class LifeInsuranceWakeUpEui : BaseEui
{
    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);

        if (msg is LifeInsuranceWakeUpClosedMessage)
            Close();
    }
}
