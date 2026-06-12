using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Defines a faction that can claim and control territories via banners.
/// This makes the list of claimable factions data-driven instead of hardcoded.
/// 
/// Declare new ones in Resources/Prototypes/_Exodus/Territory/territory_factions.yml
/// The 'color' field controls the territory ring color on BSS map and nav radar (only for the three main POI-claiming factions).
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

    // # Exodus start - faction color for territory rings on BSS map and nav radar
    /// <summary>
    /// Base color used for the territory influence rings (BSS jump map and navigation radar)
    /// when this faction controls a grid. Alpha is applied at draw time.
    /// Only the three main claimable factions (TSFMC - light blue, PDV, Khsira) have meaningful colors.
    /// </summary>
    [DataField]
    public Color Color { get; private set; } = new Color(0.7f, 0.7f, 0.7f);
    // # Exodus end - faction color for territory rings

    /// <summary>
    /// Unscaled screen-space offset for the repeated radar label.
    /// Applied after centering, so the label still rotates around its original invisible pivot.
    /// </summary>
    [DataField]
    public Vector2 RadarLabelOffset { get; private set; } = Vector2.Zero;
}
