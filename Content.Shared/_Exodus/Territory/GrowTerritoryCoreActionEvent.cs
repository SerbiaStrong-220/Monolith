using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Territory;

public sealed partial class GrowTerritoryCoreActionEvent : WorldTargetActionEvent;

[Serializable, NetSerializable]
public sealed partial class GrowTerritoryCoreDoAfterEvent : DoAfterEvent
{
    public NetCoordinates Coordinates;

    public override DoAfterEvent Clone() => this;
}
