// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server.Atmos.EntitySystems;
using Content.Shared._Exodus.Economy;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Cargo.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Gas market keys shared by Edison gas-sale and filled canister/tank appraisal.
/// Key format: gas:Oxygen, gas:Plasma, …
/// </summary>
public sealed partial class DynamicMarketSystem
{
    [Dependency] private AtmosphereSystem _atmos = default!;

    /// <summary>
    /// Legacy lot size passed by gas-trading callers; integrated pricing is independent of lot boundaries.
    /// </summary>
    public const int GasLotMoles = 100;

    public static string GasKey(Gas gas) => $"gas:{gas}";

    public static string GasKey(int gasId) => GasKey((Gas)gasId);

    /// <summary>
    /// Dominant gas market key for a mixture (for cargo catalog correlation).
    /// </summary>
    public string? TryGetDominantGasMarketKey(GasMixture mixture)
    {
        var best = -1;
        var bestMoles = 0f;
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var moles = mixture.GetMoles(i);
            if (float.IsFinite(moles) && moles > bestMoles)
            {
                bestMoles = moles;
                best = i;
            }
        }

        return best >= 0 && bestMoles > 0 ? GasKey(best) : null;
    }

    /// <summary>
    /// Price a gas mixture with per-gas sector factors integrated over its actual moles.
    /// Same keys as Edison console — dumping O2 at Edison lowers O2 canister gas value.
    /// </summary>
    /// <param name="usePurity">Canister appraisal uses purity; Edison sale uses no purity (NF/Mono).</param>
    public double CalculateGasMixtureSellValue(
        GasMixture mixture,
        double consoleMod,
        MarketTransactionState? tx,
        bool applyImpact,
        bool usePurity)
    {
        if (mixture.TotalMoles <= 0 || !double.IsFinite(consoleMod) || consoleMod <= 0)
            return 0;

        double totalMoles = 0;
        double maxComponent = 0;
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var m = mixture.GetMoles(i);
            if (!float.IsFinite(m) || m <= 0)
                continue;
            totalMoles += m;
            if (m > maxComponent)
                maxComponent = m;
        }

        var purity = 1.0;
        if (usePurity && totalMoles > 0)
            purity = maxComponent / totalMoles;

        tx ??= new MarketTransactionState();
        double total = 0;

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var moles = mixture.GetMoles(i);
            if (!float.IsFinite(moles) || moles <= 0)
                continue;

            var unitBase = _atmos.GetGas(i).PricePerMole * purity;
            total += CalculateSequentialSellValue(
                GasKey(i),
                unitBase,
                moles,
                GasLotMoles,
                consoleMod,
                tx,
                applyImpact);
        }

        return total;
    }

    /// <summary>
    /// Build UI lines for gas sale console (appraisal + trends).
    /// </summary>
    public List<GasMarketLine> BuildGasMarketLines(
        GasMixture mixture,
        double consoleMod,
        bool usePurity)
    {
        var lines = new List<GasMarketLine>();
        if (mixture.TotalMoles <= 0 || !double.IsFinite(consoleMod) || consoleMod <= 0)
            return lines;

        double totalMoles = 0;
        double maxComponent = 0;
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var m = mixture.GetMoles(i);
            if (!float.IsFinite(m) || m <= 0)
                continue;
            totalMoles += m;
            if (m > maxComponent)
                maxComponent = m;
        }

        var purity = 1.0;
        if (usePurity && totalMoles > 0)
            purity = maxComponent / totalMoles;

        // Shadow tx so sequential display matches multi-gas appraisal order without committing.
        var tx = new MarketTransactionState();
        double cumulativeTotal = 0;
        var displayedTotal = 0;

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var moles = mixture.GetMoles(i);
            if (!float.IsFinite(moles) || moles <= 0)
                continue;

            var unitBase = _atmos.GetGas(i).PricePerMole * purity;
            var key = GasKey(i);
            var lineTotal = CalculateSequentialSellValue(
                key,
                unitBase,
                moles,
                GasLotMoles,
                consoleMod,
                tx,
                applyImpact: false);

            TryGetQuote(key, out var quote);
            cumulativeTotal += lineTotal;
            var roundedTotal = RoundSellPayout(cumulativeTotal);

            lines.Add(new GasMarketLine
            {
                GasId = i,
                Moles = moles,
                UnitPrice = lineTotal / moles,
                LineTotal = roundedTotal - displayedTotal,
                Trend = quote.Trend,
                ChangePercent = quote.ChangePercent,
            });
            displayedTotal = roundedTotal;
        }

        return lines;
    }

    /// <summary>
    /// Sell valuation for gas canisters/tanks: shell at its own prototype quote, gas at gas:* quotes.
    /// The shell appraisal can include vending discounts; traces of expensive gas cannot reprice the shell.
    /// </summary>
    public double CalculateGasContainerSellValue(
        EntityUid uid,
        GasMixture air,
        double consoleMod,
        MarketTransactionState? tx,
        bool applyImpact,
        bool usePurity,
        double? shellBasePrice = null)
    {
        if (!double.IsFinite(consoleMod) || consoleMod <= 0)
            return 0;

        var shell = shellBasePrice ?? CompOrNull<StaticPriceComponent>(uid)?.Price ?? 0;
        tx ??= new MarketTransactionState();

        var gas = CalculateGasMixtureSellValue(air, consoleMod, tx, applyImpact, usePurity);
        if (Prototype(uid) is { } prototype)
        {
            shell = CalculateSequentialSellValue(ProtoKey(prototype.ID), shell, 1, 1, consoleMod, tx, applyImpact);
        }
        else
        {
            shell = double.IsFinite(shell) ? Math.Max(0, shell) * consoleMod : 0;
        }

        return shell + gas;
    }

    /// <summary>
    /// Price catalog quantities, accounting for the units in each spawned stack and keeping gas
    /// container shells separate from their contents without changing the catalog's nominal price.
    /// </summary>
    public double CalculatePrototypeBuyCost(
        EntProtoId prototypeId,
        double entityBasePrice,
        int entityCount,
        double consoleMod,
        MarketTransactionState? tx,
        bool applyImpact)
    {
        if (!double.IsFinite(entityBasePrice) || !double.IsFinite(consoleMod) ||
            entityBasePrice <= 0 || entityCount <= 0 || consoleMod <= 0)
        {
            return 0;
        }

        if (!_enabled)
            return entityBasePrice * entityCount * consoleMod;

        GasMixture? air = null;
        double nominalShell = 0;
        if (_prototypes.TryIndex(prototypeId, out var prototype))
        {
            if (prototype.TryGetComponent<GasCanisterComponent>(out var canister, _factory))
                air = canister.Air;
            else if (prototype.TryGetComponent<GasTankComponent>(out var tank, _factory))
                air = tank.Air;

            if (prototype.TryGetComponent<StaticPriceComponent>(out var staticPrice, _factory))
                nominalShell = Math.Max(0, staticPrice.Price);
        }

        double nominalGas = 0;
        if (air != null)
        {
            for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
            {
                var moles = air.GetMoles(i);
                if (float.IsFinite(moles) && moles > 0)
                    nominalGas += (double)moles * Math.Max(0, _atmos.GetGas(i).PricePerMole);
            }
        }

        if (air == null || nominalGas <= 0 || !double.IsFinite(nominalGas))
        {
            var units = GetUnitCountForPrototype(prototypeId);
            var key = air == null ? GetMarketKeyFromPrototype(prototypeId) : ProtoKey(prototypeId.Id);
            return CalculateSequentialBuyCost(key, entityBasePrice / units, (double)entityCount * units,
                GetLotSizeForPrototype(prototypeId), consoleMod, tx, applyImpact);
        }

        tx ??= new MarketTransactionState();
        var shell = Math.Min(entityBasePrice, nominalShell);
        var gas = Math.Max(0, entityBasePrice - shell);
        var total = CalculateSequentialBuyCost(ProtoKey(prototypeId.Id), shell, entityCount, 1, consoleMod, tx, applyImpact);
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            var moles = air.GetMoles(i);
            if (!float.IsFinite(moles) || moles <= 0)
                continue;

            var unitPrice = gas * _atmos.GetGas(i).PricePerMole / nominalGas;
            total += CalculateSequentialBuyCost(GasKey(i), unitPrice, (double)moles * entityCount,
                GasLotMoles, consoleMod, tx, applyImpact);
        }

        return total;
    }
}
