using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public partial class EXCVars
{
    public static readonly CVarDef<bool> ParallelMoverUpdate =
        CVarDef.Create("exds.parallel_mover_update", false, CVar.SERVERONLY);
}
