using Content.Shared._NF.Bank.Components;
using Content.Shared._Mono.CCVar;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Configuration;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Bank;

[NetSerializable, Serializable]
public enum BankATMMenuUiKey : byte
{
    ATM,
    BlackMarket
}

public abstract partial class SharedBankSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ItemSlotsSystem _itemSlotsSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BankATMComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<BankATMComponent, ComponentRemove>(OnComponentRemove);
        SubscribeLocalEvent<StationBankATMComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<StationBankATMComponent, ComponentRemove>(OnComponentRemove);
    }

    private void OnComponentInit(EntityUid uid, BankATMComponent component, ComponentInit args)
    {
        _itemSlotsSystem.AddItemSlot(uid, BankATMComponent.CashSlotId, component.CashSlot);
    }

    private void OnComponentRemove(EntityUid uid, BankATMComponent component, ComponentRemove args)
    {
        _itemSlotsSystem.RemoveItemSlot(uid, component.CashSlot);
    }

    private void OnComponentInit(EntityUid uid, StationBankATMComponent component, ComponentInit args)
    {
        _itemSlotsSystem.AddItemSlot(uid, StationBankATMComponent.CashSlotId, component.CashSlot);
    }

    private void OnComponentRemove(EntityUid uid, StationBankATMComponent component, ComponentRemove args)
    {
        _itemSlotsSystem.RemoveItemSlot(uid, component.CashSlot);
    }

    // Exodus-Start
    /// <summary>
    /// Computes how much of a deposit lands on the main bank account (<paramref name="amount"/>)
    /// versus how much is taxed away into savings (<paramref name="taxedAway"/>).
    /// The marginal tax rate is 0% up to <see cref="MonoCVars.DepositThreshold"/>, then rises
    /// linearly with the running balance up to <see cref="MonoCVars.DepositMaxRate"/> at
    /// <see cref="MonoCVars.DepositFullTaxBalance"/>. The rate is applied marginally and integrated
    /// over the balance the deposit moves through, so a single large deposit is taxed progressively.
    /// </summary>
    public void GetTaxedDepositAmount(int deposit, int balance, out int amount, out int taxedAway)
    {
        amount = 0;
        taxedAway = 0;
        if (deposit <= 0)
            return;

        var threshold = (double)_cfg.GetCVar(MonoCVars.DepositThreshold);
        var fullTaxBalance = (double)_cfg.GetCVar(MonoCVars.DepositFullTaxBalance);
        var maxRate = Math.Clamp((double)_cfg.GetCVar(MonoCVars.DepositMaxRate), 0.0, 0.999999);

        // Balance width over which the marginal rate climbs from 0% to 100%.
        var width = Math.Max(fullTaxBalance - threshold, 1.0);

        var b = (double)balance;          // running main-account balance
        var remaining = (double)deposit;  // gross cash still to process
        var net = 0.0;                    // cash that lands on the main account

        // Below the threshold: no tax, 1:1 until the balance reaches the threshold.
        if (b < threshold)
        {
            var pass = Math.Min(remaining, threshold - b);
            net += pass;
            b += pass;
            remaining -= pass;
        }

        // Balance at which the marginal rate reaches the cap.
        var capBalance = threshold + maxRate * width;

        // Linear region: marginal rate = (b - threshold) / width.
        // ODE db/dg = 1 - (b - threshold)/width  =>  b(g) = A - (A - b) * e^(-g/width), A = threshold + width.
        if (remaining > 0 && b < capBalance)
        {
            var asymptote = threshold + width; // balance where the marginal rate would be 100%
            var startGap = asymptote - b;      // > 0
            var endGap = asymptote - capBalance; // = width * (1 - maxRate) > 0
            var grossToCap = width * Math.Log(startGap / endGap);
            var g = Math.Min(remaining, grossToCap);
            var newB = asymptote - startGap * Math.Exp(-g / width);
            net += newB - b;
            b = newB;
            remaining -= g;
        }

        // Capped region: flat marginal rate = maxRate.
        if (remaining > 0)
            net += remaining * (1.0 - maxRate);

        amount = (int)Math.Clamp(Math.Round(net), 0, deposit);
        taxedAway = deposit - amount;
    }
    // Exodus-End
}

