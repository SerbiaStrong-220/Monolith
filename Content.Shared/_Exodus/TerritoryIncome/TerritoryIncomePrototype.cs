using Content.Shared._Exodus.Territory;
using Content.Shared.NPC.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.TerritoryIncome;

/// <summary>
/// A round-local faction fund paid for holding territory. Terminals share this account.
/// </summary>
[Prototype]
public sealed partial class TerritoryIncomePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = string.Empty;

    /// <summary>Localized account title.</summary>
    [DataField(required: true)]
    public LocId Name = string.Empty;

    /// <summary>Territory controller whose captures generate income.</summary>
    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> Faction;

    /// <summary>NPC faction membership required to access the fund.</summary>
    [DataField(required: true)]
    public ProtoId<NpcFactionPrototype> AccessFaction;

    /// <summary>Physical currency stack; one stack unit equals one account unit.</summary>
    [DataField(required: true)]
    public ProtoId<StackPrototype> CurrencyStack;

    /// <summary>Localized short currency name used by the terminal.</summary>
    [DataField(required: true)]
    public LocId CurrencyName = string.Empty;

    /// <summary>Currency earned by one influence point held for an entire interval.</summary>
    [DataField]
    public int CurrencyPerPoint = 5;

    /// <summary>Time between payments into the withdrawable balance.</summary>
    [DataField]
    public TimeSpan PayoutInterval = TimeSpan.FromMinutes(10);

    /// <summary>Limit on a single withdrawal to bound physical item spawning.</summary>
    [DataField]
    public int MaxWithdrawal = 1000;

    /// <summary>Exclude starting bases, even if they change hands later in the round.</summary>
    [DataField]
    public bool ExcludeInitialClaims = true;
}
