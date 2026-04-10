using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public partial class XCVars
{
    public static readonly CVarDef<bool> BulletCounterEnabled =
        CVarDef.Create("exds.bullet_counter_enabled", true, CVar.SERVERONLY);
}
