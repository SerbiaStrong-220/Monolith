using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Movement;

[RegisterComponent]
public sealed partial class TemporarySpeedBoostComponent : Component
{
    [DataField(required: true)]
    public EntProtoId<InstantActionComponent> Action = default!;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(5);

    [DataField]
    public float WalkSpeedMultiplier = 1f;

    [DataField]
    public float SprintSpeedMultiplier = 1f;

    [DataField]
    public float WeightlessSpeedMultiplier = 1f;

    [DataField]
    public float WeightlessAccelerationMultiplier = 1f;

    [ViewVariables]
    public TimeSpan? EndsAt;

    [ViewVariables]
    public EntityUid? ActionEntity;
}

public sealed partial class TemporarySpeedBoostActionEvent : InstantActionEvent;
