using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public sealed partial class EXCVars
{
    /// <summary>
    /// Tiles a bulk mining safety check may visit around a cut before falling back to the shared, time-sliced
    /// articulation analysis. Resolves most surface cuts immediately. Zero always uses the full analysis.
    /// </summary>
    public static readonly CVarDef<int> BulkMiningLocalSearchBudget =
        CVarDef.Create("exds.bulk_mining_local_search_budget", 384, CVar.SERVERONLY);

    /// <summary>Refinery bonus granted by the first linked ship of a bulk mining consortium.</summary>
    public static readonly CVarDef<float> BulkMiningLinkBonus =
        CVarDef.Create("exds.bulk_mining_link_bonus", 0.15f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Each further ship adds the previous ship's bonus multiplied by this factor (diminishing returns).</summary>
    public static readonly CVarDef<float> BulkMiningLinkBonusDecay =
        CVarDef.Create("exds.bulk_mining_link_bonus_decay", 0.5f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Upper bound of the consortium bonus, as a fraction.</summary>
    public static readonly CVarDef<float> BulkMiningLinkMaxBonus =
        CVarDef.Create("exds.bulk_mining_link_max_bonus", 0.3f, CVar.SERVER | CVar.REPLICATED);
}
