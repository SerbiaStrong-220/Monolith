using Content.Shared._Exodus.War;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.War;

/// <summary>
/// Runtime state for the global faction escalation code.
/// </summary>
[RegisterComponent]
public sealed partial class FactionAlertLevelComponent : Component
{
    [DataField(required: true)]
    public ProtoId<FactionAlertLevelPrototype> Prototype = "factionAlerts";

    [ViewVariables]
    public string CurrentLevel = "hestia";

    [ViewVariables]
    public string? PendingLevel;

    [ViewVariables]
    public TimeSpan PendingAt;

    [ViewVariables]
    public TimeSpan NextChangeAt;

    [ViewVariables]
    public bool RoundInitialized;
}
