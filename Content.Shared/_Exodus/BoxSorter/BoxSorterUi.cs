using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.BoxSorter;

[Serializable, NetSerializable]
public enum BoxSorterUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class BoxSorterUiState : BoundUserInterfaceState
{
    public readonly int? OtherRoute;
    public readonly Dictionary<NetEntity, string> Destinations;
    public readonly Dictionary<NetEntity, int> DestinationRoutes;

    public BoxSorterUiState(
        int? otherRoute,
        Dictionary<NetEntity, string> destinations,
        Dictionary<NetEntity, int> destinationRoutes)
    {
        OtherRoute = otherRoute;
        Destinations = destinations;
        DestinationRoutes = destinationRoutes;
    }
}

[Serializable, NetSerializable]
public sealed class BoxSorterSetOtherRouteMessage : BoundUserInterfaceMessage
{

    public readonly int? Channel;

    public BoxSorterSetOtherRouteMessage(int? channel)
    {
        Channel = channel;
    }
}

[Serializable, NetSerializable]
public sealed class BoxSorterSetDestinationRouteMessage : BoundUserInterfaceMessage
{

    public readonly NetEntity Destination;

    public readonly int? Channel;

    public BoxSorterSetDestinationRouteMessage(NetEntity destination, int? channel)
    {
        Destination = destination;
        Channel = channel;
    }
}
