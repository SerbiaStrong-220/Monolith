using Content.Server._NF.Bank;
using Content.Server.Stack;
using Content.Server._Exodus.Economy; // Exodus dynamic market
using Content.Shared._NF.Market;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Market.Systems;

public sealed partial class MarketSystem: SharedMarketSystem
{
    [Dependency] private BankSystem _bankSystem = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    // Exodus: admission rules and appraisal dependencies belong to MarketStockIntakeSystem.
    [Dependency] private StackSystem _stackSystem = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DynamicMarketSystem _dynamicMarket = default!; // Exodus dynamic market

    public override void Initialize()
    {
        base.Initialize();

        InitializeConsole();
        InitializeCrateMachine();
    }
}
