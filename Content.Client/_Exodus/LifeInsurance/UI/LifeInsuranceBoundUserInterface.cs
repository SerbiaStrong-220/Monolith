using Content.Shared._Exodus.LifeInsurance;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.LifeInsurance.UI;

[UsedImplicitly]
public sealed class LifeInsuranceBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private LifeInsuranceWindow? _window;

    public LifeInsuranceBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<LifeInsuranceWindow>();
        _window.OnRecordDna += () => SendMessage(new LifeInsuranceRecordDnaMessage());
        _window.OnBuy += userId => SendMessage(new LifeInsuranceBuyMessage(userId));
        _window.OnDelete += userId => SendMessage(new LifeInsuranceDeleteMessage(userId));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is LifeInsuranceConsoleState casted)
            _window?.Populate(casted);
    }
}
