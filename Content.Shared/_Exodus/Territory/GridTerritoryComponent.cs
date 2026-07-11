using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Marks a grid as having a controllable territory / influence zone on the nav radar.
/// 
/// Core design (per requirements):
/// - Every station/POI is unclaimed by default.
/// - A station of any type will NEVER belong to a faction by default (ontologically impossible).
/// - If a station must start under a faction's control, place the faction's banner entity
///   (with TerritoryBanner in yaml / TerritoryBannerComponent in C#) anchored on the grid directly in the map file.
///   The banner system will pick it up on load via SetController.
/// 
/// - controllingFaction: current runtime owner (updated when banners are placed/removed).
///   Do not set this field in map yaml to establish "default" ownership.
/// 
/// - Radar claim text always comes from the active TerritoryFactionPrototype's radarLabel LocId.
/// 
/// - defaultLabel: text shown when unclaimed (ControllingFaction is null).
///   Defaults to "territory-unclaimed".
///   Can be overridden per-grid for special neutral text.
///
/// - claimable: whether banners can claim this territory.
///   Some service or hub territories are display-only and cannot be controlled by factions.
/// 
/// Effective radius comes from the server-side territory profile.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GridTerritoryComponent : Component
{
    /// <summary>
    /// Effective radius of the territory circle in world units.
    /// Server applies this from the territory profile resolved by game map / station id.
    /// </summary>
    [AutoNetworkedField]
    public float Radius;

    /// <summary>
    /// Whether POI icon/label colors should follow territory ownership.
    /// Server applies this from the territory profile resolved by game map / station id.
    /// </summary>
    [AutoNetworkedField]
    public bool ColorPoiByFaction;

    /// <summary>
    /// POI icon/label color while unclaimed.
    /// Server applies this from the territory profile resolved by game map / station id.
    /// </summary>
    [AutoNetworkedField]
    public Color NeutralPoiColor;

    /// <summary>
    /// Whether territory banners can claim this grid.
    /// Server applies this from the territory profile resolved by game map / station id.
    /// </summary>
    [AutoNetworkedField]
    public bool Claimable = true;

    /// <summary>
    /// Minimum fraction of snapshotted SRD-repairable entities that must still be anchored on this grid
    /// before a territory banner can be anchored to claim it.
    /// Server applies this from the territory profile resolved by game map / station id.
    /// Zero disables the integrity gate.
    /// </summary>
    [AutoNetworkedField]
    public float MinClaimRepairIntegrity = 0f;

    /// <summary>
    /// The faction currently controlling this territory.
    /// Defined in TerritoryFactionPrototype (data-driven config under _Exodus).
    /// Null / unset = neutral.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<TerritoryFactionPrototype>? ControllingFaction = null;

    /// <summary>
    /// Label to use when there is no controlling faction (neutral state).
    /// Defaults to the unclaimed key.
    /// Mappers can override per-POI (e.g. station name) if desired.
    /// </summary>
    [DataField, AutoNetworkedField]
    public LocId DefaultLabel = "territory-unclaimed";

    /// <summary>
    /// Entity prototype spawned at the territory center to provide biome/ambient selection.
    /// Server applies this from the territory profile resolved by game map / station id.
    /// </summary>
    public ProtoId<EntityPrototype>? BiomeSourcePrototype;

    /// <summary>
    /// Runtime biome source spawned for this territory grid.
    /// </summary>
    [DataField, NonSerialized]
    public EntityUid? ActiveBiomeSource = null;

    /// <summary>
    /// The entity currently providing the active claim (the anchored banner).
    /// Server-authoritative. Used to know which banner to "remove" to clear the claim.
    /// </summary>
    [DataField, AutoNetworkedField, NonSerialized]
    public EntityUid? ActiveClaimBanner = null;
}
