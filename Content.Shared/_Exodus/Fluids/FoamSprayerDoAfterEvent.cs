using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Fluids;

[Serializable, NetSerializable]
public sealed partial class FoamSprayerDoAfterEvent : DoAfterEvent
{
    public NetCoordinates StartCoordinates;

    public override DoAfterEvent Clone() => new FoamSprayerDoAfterEvent
    {
        StartCoordinates = StartCoordinates,
    };
}
