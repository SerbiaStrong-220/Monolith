using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public sealed partial class EXCVars
{
    /// <summary>Total colony member/cell operations allowed per server tick, shared fairly between cores.</summary>
    public static readonly CVarDef<int> RotNetworkBudget =
        CVarDef.Create("exds.rot_network_budget", 4096, CVar.SERVERONLY);

    public static readonly CVarDef<int> RotSpreadBudget =
        CVarDef.Create("exds.rot_spread_budget", 128, CVar.SERVERONLY);

    public static readonly CVarDef<int> RotSpreadMutationBudget =
        CVarDef.Create("exds.rot_spread_mutation_budget", 4, CVar.SERVERONLY);

    /// <summary>
    /// Living rot creatures per grid (or per map in open space) above which nests and nurseries stop producing.
    /// Zero disables the cap.
    /// </summary>
    public static readonly CVarDef<int> RotPopulationCap =
        CVarDef.Create("exds.rot_population_cap", 40, CVar.SERVERONLY);
}
