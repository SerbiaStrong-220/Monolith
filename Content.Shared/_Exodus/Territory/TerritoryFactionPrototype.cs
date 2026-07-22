using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Defines a faction that can claim and control territories via banners.
/// This makes the list of claimable factions data-driven instead of hardcoded.
/// 
/// Declare new ones in Resources/Prototypes/_Exodus/Territory/territory_factions.yml
/// The 'color' field controls the territory ring color on BSS map and nav radar (main claim factions and side claim factions).
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

    /// <summary>
    /// Optional NPC faction applied to a grid while this territory faction controls it.
    /// This makes captured territories friendly to matching factional AI cores.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype>? NpcFaction { get; private set; }

    /// <summary>
    /// Controls how this faction's captured territory score affects faction store discounts.
    /// Negative factions penalize every other faction individually, while positive factions only boost themselves.
    /// </summary>
    [DataField]
    public TerritoryDiscountAlignment DiscountAlignment { get; private set; } = TerritoryDiscountAlignment.Positive;

    /// <summary>
    /// Optional cooldown before this faction can claim another territory.
    /// If unset, territoryClaimRules.defaultFactionClaimCooldown is used.
    /// Zero disables per-faction claim cooldown for this faction.
    /// </summary>
    [DataField]
    public TimeSpan? ClaimCooldown { get; private set; }

    // # Exodus start - faction color for territory rings on BSS map and nav radar
    /// <summary>
    /// Base color used for the territory influence rings (BSS jump map and navigation radar)
    /// when this faction controls a grid. Alpha is applied at draw time.
    /// Main claim factions (TSFMC, PDV, Khsira) and side claim factions (Syndicate) have meaningful colors.
    /// </summary>
    [DataField]
    public Color Color { get; private set; } = new Color(0.7f, 0.7f, 0.7f);
    // # Exodus end - faction color for territory rings
}
