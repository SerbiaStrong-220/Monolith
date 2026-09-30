using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public sealed partial class EXCVars
{
    /// <summary>
    /// Minimum real-time interval, in seconds, between radar outline rebuilds of a grid whose tiles keep changing.
    /// The first outline of a grid is always built immediately.
    /// </summary>
    public static readonly CVarDef<float> RadarGridRebuildInterval =
        CVarDef.Create("exds.radar_grid_rebuild_interval", 0.25f, CVar.CLIENTONLY | CVar.ARCHIVE);
}
