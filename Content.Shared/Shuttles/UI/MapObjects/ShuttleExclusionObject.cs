using Content.Shared._Exodus.Shuttles; // Exodus hatched-ftl-zones
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.UI.MapObjects;

[Serializable, NetSerializable]
public record struct ShuttleExclusionObject(NetCoordinates Coordinates, float Range, string Name = "", ShuttleExclusionFill Fill = ShuttleExclusionFill.Solid) : IMapObject // Exodus hatched-ftl-zones: Fill
{
    public bool HideButton => false;
}
