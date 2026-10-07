using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Economy.Admin;

/// <summary>
/// Effective server-wide market configuration. Pressure follows the same curve in both directions.
/// </summary>
[Serializable, NetSerializable]
public sealed record MarketGlobalSettings(
    bool Enabled = true,
    double ImpactStrength = 0.08,
    double ReferenceVolume = 100,
    double MinFactor = 0.01,
    double MaxFactor = 9.99,
    double DecayIntervalSeconds = 30,
    double DecayRate = 0.0015,
    double PurchaseMargin = 0.05)
{
    public bool IsValid()
    {
        return double.IsFinite(ImpactStrength) && ImpactStrength >= 0 &&
               double.IsFinite(ReferenceVolume) && ReferenceVolume > 0 &&
               double.IsFinite(MinFactor) && MinFactor > 0 && MinFactor <= 1 &&
               double.IsFinite(MaxFactor) && MaxFactor >= 1 && MaxFactor <= float.MaxValue &&
               double.IsFinite(DecayIntervalSeconds) && DecayIntervalSeconds >= 1 &&
               DecayIntervalSeconds <= TimeSpan.MaxValue.TotalSeconds / 2 &&
               double.IsFinite(DecayRate) && DecayRate >= 0 && DecayRate <= 1 &&
               double.IsFinite(PurchaseMargin) && PurchaseMargin >= 0 && PurchaseMargin <= 1 &&
               double.IsFinite(ImpactStrength / ReferenceVolume);
    }
}

/// <summary>
/// Null fields inherit the current server configuration, including after a restart or config reload.
/// </summary>
[Serializable, NetSerializable]
public sealed record MarketGlobalSettingsOverride(
    bool? Enabled = null,
    double? ImpactStrength = null,
    double? ReferenceVolume = null,
    double? MinFactor = null,
    double? MaxFactor = null,
    double? DecayIntervalSeconds = null,
    double? DecayRate = null,
    double? PurchaseMargin = null)
{
    public MarketGlobalSettings Resolve(MarketGlobalSettings defaults)
    {
        return new MarketGlobalSettings(
            Enabled ?? defaults.Enabled,
            ImpactStrength ?? defaults.ImpactStrength,
            ReferenceVolume ?? defaults.ReferenceVolume,
            MinFactor ?? defaults.MinFactor,
            MaxFactor ?? defaults.MaxFactor,
            DecayIntervalSeconds ?? defaults.DecayIntervalSeconds,
            DecayRate ?? defaults.DecayRate,
            PurchaseMargin ?? defaults.PurchaseMargin);
    }
}

/// <summary>
/// Detached configuration snapshot. Its revision is independent of ordinary price movement.
/// </summary>
[Serializable, NetSerializable]
public sealed record MarketSettingsSnapshot(
    long Revision,
    MarketGlobalSettings Defaults,
    MarketGlobalSettings Global,
    MarketGlobalSettingsOverride Overrides,
    Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> GroupOverrides);
