// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

[CVarDefs]
public sealed partial class EXCVars
{
    /// <summary>
    /// Contains ID used to identify central station by station ID (from BecomesStation)
    /// </summary>
    public static readonly CVarDef<string> CentralStationId =
        CVarDef.Create("exds.central_station_id", "Frontier", CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    /// User's favorite decal colors, stored as a ';'-separated list of hex colors.
    /// Client-only and archived to client_config.toml so it survives relogs/restarts.
    /// </summary>
    public static readonly CVarDef<string> DecalFavoriteColors =
        CVarDef.Create("exds.decal_favorite_colors", "", CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> WebAPIToken =
        CVarDef.Create("exds.webapi_token", "", CVar.SERVERONLY);

    /// <summary>
    /// Price (in spesos) charged to the operator for adding a single life insurance charge.
    /// </summary>
    public static readonly CVarDef<int> LifeInsurancePrice =
        CVarDef.Create("exds.life_insurance_price", 250000, CVar.SERVERONLY);
}
