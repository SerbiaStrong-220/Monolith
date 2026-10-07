using Content.Shared._Exodus.Economy; // Exodus gas market lines
using Content.Shared.Atmos;
using Robust.Shared.Serialization;

namespace Content.Shared._NF.Atmos.BUI;

[NetSerializable, Serializable]
public sealed class GasSaleConsoleBoundUserInterfaceState(
    int appraisal,
    GasMixture mixture,
    bool enabled,
    List<GasMarketLine>? gasLines = null) // Exodus
    : BoundUserInterfaceState
{
    /// <summary>
    /// Estimated appraisal value of the gas mixture.
    /// </summary>
    public int Appraisal = appraisal;

    /// <summary>
    /// The mixture in the linked sale entity.
    /// </summary>
    public GasMixture Mixture = mixture;

    /// <summary>
    /// Whether or not the buttons on the interface are enabled.
    /// </summary>
    public bool Enabled = enabled;

    /// <summary>
    /// Exodus: per-gas market lines (moles, unit price, total, trend).
    /// </summary>
    public List<GasMarketLine>? GasLines = gasLines;
}

[Serializable, NetSerializable]
public enum GasSaleConsoleUiKey : byte
{
    Key,
}
