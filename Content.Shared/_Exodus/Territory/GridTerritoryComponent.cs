using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Marks a grid as having a controllable territory / influence zone on the nav radar.
/// 
/// Core design (per requirements):
/// - Every station/POI is "Незанято" (unclaimed) by default.
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
///   Can be overridden per-grid for special neutral text (e.g. station name "КОЛОСС-ЦЕНТРАЛ").
/// 
/// Radius: free float. Common useful values 1000/2500/5000 based on station importance.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class GridTerritoryComponent : Component
{
    /// <summary>
    /// Radius of the territory circle in world units.
    /// Recommended values: 1000 (small/outpost), 2500 (medium), 5000 (large/flagship).
    /// </summary>
    [DataField]
    public float Radius = 2500f;

    /// <summary>
    /// The faction currently controlling this territory.
    /// Defined in TerritoryFactionPrototype (data-driven config under _Exodus).
    /// Null / unset = neutral (display "Unclaimed" / "Незанято").
    /// </summary>
    [DataField]
    public ProtoId<TerritoryFactionPrototype>? ControllingFaction = null;

    /// <summary>
    /// Label to use when there is no controlling faction (neutral state).
    /// Defaults to the "Unclaimed"/"Незанято" key.
    /// Mappers can override per-POI (e.g. station name) if desired.
    /// </summary>
    [DataField]
    public LocId DefaultLabel = "territory-unclaimed";

    /// <summary>
    /// The entity currently providing the active claim (the anchored banner).
    /// Server-authoritative. Used to know which banner to "remove" to clear the claim.
    /// </summary>
    [DataField, NonSerialized]
    public EntityUid? ActiveClaimBanner = null;
}
