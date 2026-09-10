using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.TerritoryIncome;

/// <summary>A portable terminal for a shared territory-income account.</summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TerritoryIncomeTerminalComponent : Component
{
    /// <summary>The account accessed by this terminal. The terminal never owns its balance.</summary>
    [DataField(required: true)]
    public ProtoId<TerritoryIncomePrototype> Account;
}
