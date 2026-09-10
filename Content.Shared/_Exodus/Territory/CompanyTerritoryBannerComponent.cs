using Content.Shared._Mono.Company;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Marks a banner as a corporate territory claim.
/// The company is assigned from the corporation ID card when an unassigned banner is anchored.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CompanyTerritoryBannerComponent : Component
{
    /// <summary>
    /// Corporation that owns this banner. Null means that the banner has not been installed yet.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<CompanyPrototype>? Company;

    /// <summary>
    /// Faction territory selected when this banner was last successfully installed.
    /// This does not change when the faction later captures the grid.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<TerritoryFactionPrototype>? TerritoryFaction;
}
