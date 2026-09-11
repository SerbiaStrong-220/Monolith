// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Robust.Shared.Configuration;

namespace Content.Shared._Exodus.CCVar;

public sealed partial class EXCVars
{
    /// <summary>Fraction of remaining airborne infection risk blocked by working internals.</summary>
    public static readonly CVarDef<float> VirologyInternalsProtection =
        CVarDef.Create("virology.internals_protection", 0.9f, CVar.SERVER | CVar.ARCHIVE);

    /// <summary>Min crew granted roundstart virus immunity.</summary>
    public static readonly CVarDef<int> VirologyImmuneCountMin =
        CVarDef.Create("virology.immune_count_min", 1, CVar.SERVER | CVar.ARCHIVE);

    /// <summary>Max crew granted roundstart virus immunity.</summary>
    public static readonly CVarDef<int> VirologyImmuneCountMax =
        CVarDef.Create("virology.immune_count_max", 2, CVar.SERVER | CVar.ARCHIVE);
}
