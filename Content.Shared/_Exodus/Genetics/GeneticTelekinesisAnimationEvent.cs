using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Genetics;

/// <summary>Visual travel for an item placed in the world by a server-side remote interaction.</summary>
[Serializable, NetSerializable]
public sealed class GeneticTelekinesisAnimationEvent(NetEntity item, NetCoordinates start, NetCoordinates end, TimeSpan duration) : EntityEventArgs
{
    public readonly NetEntity Item = item;
    public readonly NetCoordinates Start = start;
    public readonly NetCoordinates End = end;
    public readonly TimeSpan Duration = duration;
}
