using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.ShipRepair;

/// <summary>Preview of the quoted construction, replicated on an otherwise purely visual SRD effect.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(raiseAfterAutoHandleState: true)]
public sealed partial class ShipRepairConstructionVisualsComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntProtoId? TargetPrototype;

    [DataField, AutoNetworkedField]
    public int? TileType;

    // Client-only state; no per-frame entity spawning or prototype lookups.
    public bool PreviewInitialized;
}
