using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Virology.Intelligent;

/// <summary>A nester finishing one bite of a corpse or spilled blood.</summary>
[Serializable, NetSerializable]
public sealed partial class RotNesterBiteEvent : SimpleDoAfterEvent;
