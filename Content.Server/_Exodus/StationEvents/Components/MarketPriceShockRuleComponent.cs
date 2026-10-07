// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared._Exodus.Economy;
using Robust.Shared.Audio;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.StationEvents.Components;

/// <summary>
/// Applies a one-off change to a random selection of independently traded commodities.
/// </summary>
[RegisterComponent, Access(typeof(MarketPriceShockRuleSystem))]
public sealed partial class MarketPriceShockRuleComponent : Component
{
    /// <summary>
    /// Commodity groups eligible for the shock. An empty set selects no commodities.
    /// </summary>
    [DataField(required: true)]
    public HashSet<ProtoId<MarketCommodityGroupPrototype>> AllowedGroups = new();

    /// <summary>
    /// Number of distinct market keys affected by the event.
    /// </summary>
    [DataField]
    public int ProductCount = 10;

    /// <summary>
    /// Fraction of the current price added or removed, chosen independently for each commodity.
    /// Must be greater than zero and less than one.
    /// </summary>
    [DataField]
    public double PriceChange = 0.5;

    /// <summary>
    /// Bounds basket validation work when candidates cannot be traded or have reached price limits.
    /// </summary>
    [DataField]
    public int MaxSelectionAttempts = 256;

    /// <summary>
    /// Announcement after a successful change. Receives the localized list in <c>changes</c>.
    /// </summary>
    [DataField]
    public LocId? Announcement;

    /// <summary>
    /// Localized announcement sender; null uses the standard station announcement sender.
    /// </summary>
    [DataField]
    public LocId? Sender;

    [DataField]
    public SoundSpecifier? AnnouncementSound;

    [DataField]
    public Color AnnouncementColor = Color.Gold;

    /// <summary>
    /// Prevents replaying a completed or rejected event on the same rule entity.
    /// </summary>
    [ViewVariables]
    public bool Processed;

    /// <summary>
    /// Actual changes retained for administration and diagnostics after the rule ends.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, MarketPriceShockChange> Changes = new();
}

public readonly record struct MarketPriceShockChange(string Name, double OldFactor, double NewFactor);
