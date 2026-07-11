using Content.Shared.NPC.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.NPC.Components;

[Serializable, NetSerializable]
public enum FactionAiControlState : byte
{
    Controlled,
    Contested
}

/// <summary>
/// Client-visible state for grids controlled by factional NPC AI cores.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class FactionAiControlledGridComponent : Component
{
    [AutoNetworkedField]
    public FactionAiControlState State = FactionAiControlState.Controlled;

    [AutoNetworkedField]
    public ProtoId<NpcFactionPrototype>? Faction;
}
