using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Virology.Lifecycle;

[Serializable, NetSerializable]
public sealed partial class RotSatedConsumeEvent : SimpleDoAfterEvent
{
}
