using System.Numerics;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Examine;

/// <summary>A local, temporary indicator shown when examining a machine.</summary>
[RegisterComponent]
public sealed partial class ExamineIndicatorComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Indicator;

    [DataField]
    public Vector2 Offset;

    [ViewVariables]
    public EntityUid? ActiveIndicator;
}
