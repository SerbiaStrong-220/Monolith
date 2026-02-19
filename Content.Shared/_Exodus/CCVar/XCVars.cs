// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Robust.Shared.Configuration;

namespace Content.Shared.Exodus.CCVar;

[CVarDefs]
public sealed partial class XCVars
{
    /// <summary>
    /// Contains ID used to identify central station by station ID (from BecomesStation)
    /// </summary>
    public static readonly CVarDef<string> CentralStationId =
        CVarDef.Create("exds.central_station_id", "Frontier", CVar.SERVER | CVar.REPLICATED);

}
