using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public partial class XCVars
{
    public static readonly CVarDef<float> AutoUnstuckTime =
        CVarDef.Create("exds.auto_unstuck_time", 15f, CVar.SERVERONLY);
}
