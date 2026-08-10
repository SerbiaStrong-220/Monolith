using Content.Shared.UserInterface;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.FireControl;

[Serializable, NetSerializable]
public sealed class FireControlConsoleCursorPositionMessage(NetCoordinates coordinates) : BoundUserInterfaceMessage
{
    public NetCoordinates Coordinates { get; } = coordinates;
}

[ByRefEvent]
public readonly record struct FireControlConsoleCursorPositionEvent(EntityCoordinates Coordinates);
