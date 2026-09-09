using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Opts a creature into its faction's natural-healing bonus on an actively controlled grid.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TerritoryRegenerationComponent : Component
{
    /// <summary>
    /// Territory owner required for this creature to receive the bonus.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<TerritoryFactionPrototype> Faction;
}
