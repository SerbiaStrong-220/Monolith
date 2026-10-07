// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public partial class EXCVars
{
    /// <summary>
    /// Minimum purchase margin above the global resale ceiling. Applies even when dynamic prices are disabled.
    /// </summary>
    public static readonly CVarDef<float> MarketPurchaseMargin =
        CVarDef.Create("exds.economy.market_purchase_margin", 0.05f, CVar.SERVERONLY);

    /// <summary>
    /// Master switch for the global dynamic supply/demand market.
    /// When false, buy/sell use static factors. The global purchase price floor still applies.
    /// </summary>
    public static readonly CVarDef<bool> DynamicMarketEnabled =
        CVarDef.Create("exds.economy.dynamic_market", true, CVar.SERVERONLY);

    /// <summary>
    /// Minimum sector price factor (floor after sell pressure / clamp).
    /// Default 0.01 = 1% of base price.
    /// </summary>
    public static readonly CVarDef<float> DynamicMarketMinFactor =
        CVarDef.Create("exds.economy.market_min_factor", 0.01f, CVar.SERVERONLY);

    /// <summary>
    /// Maximum sector price factor (ceiling after buy pressure / clamp).
    /// Default 9.99 = 999% of base price.
    /// </summary>
    public static readonly CVarDef<float> DynamicMarketMaxFactor =
        CVarDef.Create("exds.economy.market_max_factor", 9.99f, CVar.SERVERONLY);

    /// <summary>
    /// Sell impact strength. The larger of buy/sell impact is used in both directions so a closed
    /// trade cannot pump the factor. Each reference-volume multiplies the factor by exp(-impact).
    /// </summary>
    public static readonly CVarDef<float> DynamicMarketSellImpact =
        CVarDef.Create("exds.economy.market_sell_impact", 0.08f, CVar.SERVERONLY);

    /// <summary>
    /// Buy impact strength. The larger of buy/sell impact is used in both directions;
    /// each reference-volume of units multiplies the factor by exp(+impact).
    /// </summary>
    public static readonly CVarDef<float> DynamicMarketBuyImpact =
        CVarDef.Create("exds.economy.market_buy_impact", 0.08f, CVar.SERVERONLY);

    /// <summary>
    /// Global units that constitute one "full impact batch" for factor updates
    /// (factor *= exp(±impact * units / referenceVolume)). Default 100.
    /// </summary>
    public static readonly CVarDef<float> DynamicMarketReferenceVolume =
        CVarDef.Create("exds.economy.market_reference_volume", 100f, CVar.SERVERONLY);

    /// <summary>
    /// Seconds between mean-reversion ticks toward factor 1.0. Default 30.
    /// </summary>
    public static readonly CVarDef<float> DynamicMarketDecayIntervalSeconds =
        CVarDef.Create("exds.economy.market_decay_interval", 30f, CVar.SERVERONLY);

    /// <summary>
    /// Fraction of the gap to 1.0 closed each decay tick (default 0.0015 = 0.15% of distance per tick).
    /// </summary>
    public static readonly CVarDef<float> DynamicMarketDecayRate =
        CVarDef.Create("exds.economy.market_decay_rate", 0.0015f, CVar.SERVERONLY);

    /// <summary>
    /// Persist global market factors to the server database across rounds.
    /// </summary>
    public static readonly CVarDef<bool> DynamicMarketPersist =
        CVarDef.Create("exds.economy.market_persist", true, CVar.SERVERONLY);

    /// <summary>
    /// Seconds between dirty quote flushes to the database.
    /// </summary>
    public static readonly CVarDef<float> DynamicMarketPersistIntervalSeconds =
        CVarDef.Create("exds.economy.market_persist_interval", 60f, CVar.SERVERONLY);
}
