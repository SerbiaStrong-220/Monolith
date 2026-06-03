using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Defines a faction that can claim and control territories via banners.
/// This makes the list of claimable factions data-driven instead of hardcoded.
/// 
/// Declare new ones in Resources/Prototypes/_Exodus/Territory/territory_factions.yml
/// </summary>
[Prototype]
public sealed partial class TerritoryFactionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// LocId for the label that will be repeated on the radar circle when this faction controls a territory.
    /// Should be short and suitable for diagonal tiling.
    /// </summary>
    [DataField(required: true)]
    public LocId RadarLabel { get; private set; } = default!;

    /// <summary>
    /// Optional: the entity prototype that acts as the claim banner for this faction.
    /// Used for validation or future admin tools.
    /// </summary>
    [DataField]
    public ProtoId<EntityPrototype>? Banner { get; private set; }
}
