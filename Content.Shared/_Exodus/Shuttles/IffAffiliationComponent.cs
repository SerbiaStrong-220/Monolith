using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.Company;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Shuttles;

/// <summary>
/// Controls radar affiliation text without assigning ownership, NPC factions or corporate access.
/// Unconfigured grids retain the upstream company display; territories default to corporate control.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class IffAffiliationComponent : Component
{
    /// <summary>
    /// Source of the second IFF line. None hides it without hiding the grid name or changing its color.
    /// CorporateControl shows faction/corporate claim status only on capturable territories.
    /// </summary>
    [DataField, AutoNetworkedField]
    public IffAffiliationMode Mode = IffAffiliationMode.CorporateControl;

    /// <summary>
    /// Display affiliation and color for a grid without faction-colored territory.
    /// A faction-colored territory always uses its actual controller, including its neutral state.
    /// This does not claim territory or change NPC affiliation.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<TerritoryFactionPrototype>? Faction;

    /// <summary>
    /// Organization displayed in FixedCompany mode. Its color is deliberately ignored.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ProtoId<CompanyPrototype>? Company;

    /// <summary>
    /// Localization key displayed in FixedLabel mode, without assigning an organization or faction.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? Label;
}

[Serializable, NetSerializable]
public enum IffAffiliationMode : byte
{
    CorporateControl,
    Faction,
    FixedCompany,
    None,
    FixedLabel,
}
