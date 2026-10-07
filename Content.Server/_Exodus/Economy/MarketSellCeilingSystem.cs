// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._NF.Atmos.Components;
using Content.Server.Cargo.Components;
using Content.Shared._Crescent.Dispenser;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Cargo.Components;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Tracks the largest possible credit sale multipliers and fixed payouts, including future map endpoints.
/// Whitelists and vending discounts are deliberately ignored: they can reduce a payout, never increase it.
/// </summary>
public sealed partial class MarketSellCeilingSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;

    private readonly HashSet<Entity<CargoPalletConsoleComponent>> _pallets = new();
    private readonly HashSet<Entity<GasSaleConsoleComponent>> _gasConsoles = new();
    private readonly HashSet<Entity<DispenserComponent>> _fixedPayoutSources = new();
    private readonly Dictionary<string, double> _prototypeFixedPayouts = new(StringComparer.Ordinal);
    private readonly Dictionary<ProtoId<StackPrototype>, bool> _cashCurrencies = new();
    private EntityQuery<MarketModifierComponent> _modifierQuery;
    private double _prototypePalletMultiplier = 1;
    private double _prototypeGasMultiplier = 1;
    private double _prototypeNonCashPalletMultiplier;

    public override void Initialize()
    {
        base.Initialize();
        _modifierQuery = GetEntityQuery<MarketModifierComponent>();
        SubscribeLocalEvent<CargoPalletConsoleComponent, ComponentStartup>(OnPalletStartup);
        SubscribeLocalEvent<CargoPalletConsoleComponent, ComponentRemove>(OnPalletRemove);
        SubscribeLocalEvent<GasSaleConsoleComponent, ComponentStartup>(OnGasStartup);
        SubscribeLocalEvent<GasSaleConsoleComponent, ComponentRemove>(OnGasRemove);
        SubscribeLocalEvent<DispenserComponent, ComponentStartup>(OnDispenserStartup);
        SubscribeLocalEvent<DispenserComponent, ComponentRemove>(OnDispenserRemove);
        SubscribeLocalEvent<MarketStockSourceComponent, ComponentStartup>(OnStockSourceStartup);
        SubscribeLocalEvent<MarketStockSourceComponent, ComponentRemove>(OnStockSourceRemove);
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
        RebuildPrototypeBounds();
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        _pallets.Clear();
        _gasConsoles.Clear();
        _fixedPayoutSources.Clear();
        _prototypeFixedPayouts.Clear();
        _cashCurrencies.Clear();
        base.Shutdown();
    }

    /// <summary>
    /// Capture once at the beginning of a purchase or a batch of UI quotes and reuse throughout that batch.
    /// Only registered sale endpoints are inspected, independently of the number of items in the world.
    /// Read current component values every time: direct VV edits and dictionary edits have no change event.
    /// </summary>
    public MarketSellCeiling GetSnapshot()
    {
        var palletMultiplier = _prototypePalletMultiplier;
        var gasMultiplier = _prototypeGasMultiplier;
        var nonCashPalletMultiplier = _prototypeNonCashPalletMultiplier;
        var fixedPayouts = new Dictionary<string, double>(_prototypeFixedPayouts, StringComparer.Ordinal);

        foreach (var ent in _pallets)
        {
            var multiplier = _modifierQuery.TryGetComponent(ent, out var modifier) && !modifier.Buy
                ? ValidMultiplier(modifier.Mod)
                : 1;
            if (IsCashCurrency(ent.Comp.CashType))
            {
                palletMultiplier = Math.Max(palletMultiplier, multiplier);
            }
            else
            {
                // ItemTax always pays sector accounts in credits, independently of the seller's currency.
                nonCashPalletMultiplier = Math.Max(nonCashPalletMultiplier, multiplier);
            }
        }

        foreach (var ent in _gasConsoles)
        {
            if (!IsCashCurrency(ent.Comp.CashType) ||
                !_modifierQuery.TryGetComponent(ent, out var modifier))
            {
                continue;
            }

            // GasSaleConsole uses Mod for selling even though its prototypes retain Buy=true.
            gasMultiplier = Math.Max(gasMultiplier, ValidMultiplier(modifier.Mod));
        }

        foreach (var ent in _fixedPayoutSources)
        {
            if (!ent.Comp.Initialized || ent.Comp.Deleted || !HasComp<MarketStockSourceComponent>(ent))
                continue;

            AddFixedPayouts(fixedPayouts, ent.Comp);
        }

        return new MarketSellCeiling(palletMultiplier, Math.Max(gasMultiplier, palletMultiplier),
            nonCashPalletMultiplier, fixedPayouts);
    }

    private void OnPalletStartup(Entity<CargoPalletConsoleComponent> ent, ref ComponentStartup args)
    {
        _pallets.Add(ent);
    }

    private void OnPalletRemove(Entity<CargoPalletConsoleComponent> ent, ref ComponentRemove args)
    {
        _pallets.Remove(ent);
    }

    private void OnGasStartup(Entity<GasSaleConsoleComponent> ent, ref ComponentStartup args)
    {
        _gasConsoles.Add(ent);
    }

    private void OnGasRemove(Entity<GasSaleConsoleComponent> ent, ref ComponentRemove args)
    {
        _gasConsoles.Remove(ent);
    }

    private void OnDispenserStartup(Entity<DispenserComponent> ent, ref ComponentStartup args)
    {
        if (HasComp<MarketStockSourceComponent>(ent))
            _fixedPayoutSources.Add(ent);
    }

    private void OnDispenserRemove(Entity<DispenserComponent> ent, ref ComponentRemove args)
    {
        _fixedPayoutSources.Remove(ent);
    }

    private void OnStockSourceStartup(Entity<MarketStockSourceComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<DispenserComponent>(ent, out var dispenser))
            _fixedPayoutSources.Add((ent.Owner, dispenser));
    }

    private void OnStockSourceRemove(Entity<MarketStockSourceComponent> ent, ref ComponentRemove args)
    {
        if (TryComp<DispenserComponent>(ent, out var dispenser))
            _fixedPayoutSources.Remove((ent.Owner, dispenser));
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>() || args.WasModified<StackPrototype>())
            RebuildPrototypeBounds();
    }

    private void RebuildPrototypeBounds()
    {
        _cashCurrencies.Clear();
        _prototypePalletMultiplier = 1;
        _prototypeGasMultiplier = 1;
        _prototypeNonCashPalletMultiplier = 0;
        _prototypeFixedPayouts.Clear();

        foreach (var prototype in _prototypes.EnumeratePrototypes<EntityPrototype>())
        {
            if (!prototype.Abstract && prototype.TryGetComponent<MarketStockSourceComponent>(out _, _factory) &&
                prototype.TryGetComponent<DispenserComponent>(out var dispenser, _factory))
            {
                AddFixedPayouts(_prototypeFixedPayouts, dispenser);
            }

            if (prototype.TryGetComponent<CargoPalletConsoleComponent>(out var pallet, _factory))
            {
                var palletMultiplier = prototype.TryGetComponent<MarketModifierComponent>(out var palletModifier, _factory) &&
                                       !palletModifier.Buy
                    ? ValidMultiplier(palletModifier.Mod)
                    : 1;
                if (IsCashCurrency(pallet.CashType))
                    _prototypePalletMultiplier = Math.Max(_prototypePalletMultiplier, palletMultiplier);
                else
                    _prototypeNonCashPalletMultiplier = Math.Max(_prototypeNonCashPalletMultiplier, palletMultiplier);
            }

            if (prototype.TryGetComponent<MarketModifierComponent>(out var modifier, _factory))
            {
                var multiplier = ValidMultiplier(modifier.Mod);
                if (prototype.TryGetComponent<GasSaleConsoleComponent>(out var gas, _factory) &&
                    IsCashCurrency(gas.CashType))
                {
                    _prototypeGasMultiplier = Math.Max(_prototypeGasMultiplier, multiplier);
                }
            }
        }
    }

    private void AddFixedPayouts(Dictionary<string, double> payouts, DispenserComponent dispenser)
    {
        // DefaultItem is a free interaction, not a payment for goods. Only explicit exchanges count.
        foreach (var (inputId, rewardId) in dispenser.Inventory)
        {
            if (string.IsNullOrWhiteSpace(inputId) || string.IsNullOrWhiteSpace(rewardId) ||
                !_prototypes.TryIndex<EntityPrototype>(inputId, out var input) ||
                !_prototypes.TryIndex<EntityPrototype>(rewardId, out var reward) ||
                !reward.TryGetComponent<CashComponent>(out _, _factory) ||
                !reward.TryGetComponent<StackComponent>(out var cash, _factory) || cash.Count <= 0)
            {
                continue;
            }

            // The dispenser accepts a whole entity, even after a stack has been split to one unit.
            // All variants of that stack therefore share the largest fixed credit payout per unit.
            var key = input.TryGetComponent<StackComponent>(out var stack, _factory)
                ? DynamicMarketSystem.StackKey(stack.StackTypeId)
                : DynamicMarketSystem.ProtoKey(input.ID);
            payouts[key] = Math.Max(payouts.GetValueOrDefault(key), cash.Count);
        }
    }

    private bool IsCashCurrency(ProtoId<StackPrototype> stackId)
    {
        if (_cashCurrencies.TryGetValue(stackId, out var cash))
            return cash;

        // Identify money by its depositable Cash component, not by one hardcoded stack ID.
        cash = _prototypes.TryIndex(stackId, out var stack) &&
               _prototypes.TryIndex(stack.Spawn, out var entity) &&
               entity.TryGetComponent<CashComponent>(out _, _factory);
        _cashCurrencies[stackId] = cash;
        return cash;
    }

    private static double ValidMultiplier(float value)
    {
        return float.IsFinite(value) && value > 1 ? value : 1;
    }
}
