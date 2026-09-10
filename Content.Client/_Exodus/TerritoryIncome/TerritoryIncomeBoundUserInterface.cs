using Content.Shared._Exodus.TerritoryIncome;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.TerritoryIncome;

public sealed class TerritoryIncomeBoundUserInterface : BoundUserInterface
{
    private TerritoryIncomeWindow? _window;

    public TerritoryIncomeBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<TerritoryIncomeWindow>();
        _window.Withdraw += amount => SendMessage(new TerritoryIncomeWithdrawMessage(amount));
        if (State is TerritoryIncomeUiState state)
            _window.UpdateState(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is TerritoryIncomeUiState income)
            _window?.UpdateState(income);
    }
}
