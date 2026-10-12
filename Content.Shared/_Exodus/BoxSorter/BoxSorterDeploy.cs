using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.BoxSorter;

[Serializable, NetSerializable]
public sealed partial class BoxSorterDeployDoAfterEvent : SimpleDoAfterEvent
{
}
