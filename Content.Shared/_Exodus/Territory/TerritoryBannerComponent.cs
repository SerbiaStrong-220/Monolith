using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Attached to banner entities (in yaml as `type: TerritoryBanner`) that can claim control of a grid's territory for a faction.
/// When an entity with this component is anchored on a grid that has a GridTerritoryComponent,
/// it can become the ActiveClaimBanner and set the ControllingFaction + radar label.
/// 
/// Only one such banner should be the active claim per grid (enforced at construction + runtime).
/// 
/// For factions without final sprites yet (Империя Кхси'Ра, possibly Колосс-Централ), 
/// a placeholder/zaty chka banner entity id is used temporarily.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class TerritoryBannerComponent : Component
{
    /// <summary>
    /// The territory faction this banner claims for.
    /// See TerritoryFactionPrototype for the data-driven list of claimable factions.
    /// </summary>
    [DataField(required: true)]
    public ProtoId<TerritoryFactionPrototype> Faction = default!;
}
