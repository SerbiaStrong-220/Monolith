// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Economy;

/// <summary>
/// Global market quote for a single market key (shared across all trade terminals).
/// Factor is relative to prototype base price (1.0 = base).
/// </summary>
[Serializable, NetSerializable]
public struct MarketQuote
{
    /// <summary>
    /// Sector-wide price multiplier relative to base appraisal / catalog cost.
    /// </summary>
    public double Factor;

    /// <summary>
    /// Last-step factor delta for UI: positive = rising, negative = falling
    /// (current Factor − previous Factor; not EMA-smoothed).
    /// </summary>
    public float Trend;

    /// <summary>
    /// Factor value from the previous update step (used to compute <see cref="Trend"/>).
    /// </summary>
    public double PreviousFactor;

    public MarketQuote(double factor = 1.0)
    {
        Factor = factor;
        PreviousFactor = factor;
        Trend = 0f;
    }

    /// <summary>
    /// Percent change vs base (1.0). e.g. factor 1.1 → +10.
    /// </summary>
    public double ChangePercent => (Factor - 1.0) * 100.0;
}
