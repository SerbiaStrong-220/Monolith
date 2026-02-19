// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Shared.Actions;
using Content.Shared.FixedPoint;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Exodus.Gimmicks.Pheromones;

[RegisterComponent, NetworkedComponent]
public sealed partial class PheromonesCommunicationComponent : Component
{
    [DataField]
    public EntProtoId ActionPrototype = "ActionPheromones";

    [DataField]
    public EntityUid? ActionEntity;

    [DataField]
    public FixedPoint2 Range = 1.5f;

    [DataField]
    public Color Color = Color.Yellow;

    /// <summary>
    /// Entity created when kidan is tries to create pheromones message without any entity
    /// </summary>
    [DataField]
    public EntProtoId PheromonesCloud = "PheromonesEffect";
}

public sealed partial class TryMarkWithPheromonesEvent : EntityWorldTargetActionEvent
{
}
