using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public partial class XCVars
{
    public static readonly CVarDef<bool> ParallelMoverUpdate =
        CVarDef.Create("exds.parallel_mover_update", false, CVar.SERVERONLY);
}
