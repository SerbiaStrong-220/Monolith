using Content.Client._Exodus.Gimmicks.SlotMachine.Ui;
using Content.Shared._Exodus.Gimmicks.SlotMachine;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.Gimmicks.SlotMachine;

public sealed class SlotMachineBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SlotMachineWindow? _window;

    public SlotMachineBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<SlotMachineWindow>();
        _window.OnSpin += bet => SendMessage(new SlotMachineSpinMessage(bet));
        _window.OnInsert += amount => SendMessage(new SlotMachineInsertMessage(amount));
        _window.OnCollect += () => SendMessage(new SlotMachineCollectMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is SlotMachineBoundUserInterfaceState slotState)
            _window?.UpdateState(slotState);
    }
}
