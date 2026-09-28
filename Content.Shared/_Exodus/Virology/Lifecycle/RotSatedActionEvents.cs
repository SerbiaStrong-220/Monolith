using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Virology.Lifecycle;

public sealed partial class RotSatedConsumeActionEvent : EntityTargetActionEvent;
public sealed partial class RotSatedStopActionEvent : InstantActionEvent;
public sealed partial class RotSatedStrikeActionEvent : InstantActionEvent;

[Serializable, NetSerializable]
public sealed partial class RotSatedStripEvent : SimpleDoAfterEvent;
