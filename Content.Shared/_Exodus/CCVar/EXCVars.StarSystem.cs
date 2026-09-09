using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public sealed partial class EXCVars
{
    /// <summary>
    /// Star system generated on the main round map before preset rules start.
    /// An empty value disables automatic generation; a system configured on the map takes precedence.
    /// </summary>
    public static readonly CVarDef<string> DefaultStarSystem =
        CVarDef.Create("exds.default_star_system", "SystemKyphrus", CVar.SERVERONLY);
}
