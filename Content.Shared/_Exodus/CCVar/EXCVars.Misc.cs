using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public partial class EXCVars
{
    public static readonly CVarDef<bool> ParallelMoverUpdate =
        CVarDef.Create("exds.parallel_mover_update", false, CVar.SERVERONLY);

    public static readonly CVarDef<int> ParallelMoverThreads =
        CVarDef.Create("exds.parallel_mover_threads", 4, CVar.SERVERONLY);
}
