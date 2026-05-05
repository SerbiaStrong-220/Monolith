using System.Linq;
using System.Numerics;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Radar;

[Serializable, NetSerializable]
public enum RadarBlipShape
{
    Circle,
    Square,
    GridAlignedBox,
    Triangle,
    Star,
    Diamond,
    Hexagon,
    Arrow,
    Ring,
    // Exodus-begin nebula-radar-visualization
    NebulaPolygon, // Exodus nebula-radar-visualization
    // Exodus-end
    TerritoryCircle // Exodus territory-marker
}

[Serializable, NetSerializable]
public sealed class GiveBlipsEvent : EntityEventArgs
{
    /// <summary>
    /// Palette of blip configs, basically an int->config map.
    /// </summary>
    public readonly List<BlipConfig> ConfigPalette;

    /// <summary>
    /// Blips are now (position, velocity, scale, color, shape).
    /// </summary>
    public readonly List<BlipNetData> Blips;

    /// <summary>
    /// Hitscan lines to display on the radar as (start position, end position, thickness, color).
    /// </summary>
    public readonly List<HitscanNetData> HitscanLines;

    // Exodus-begin nebula-ftl-map
    public readonly int? RequestedMapId;
    public readonly bool NebulaOnly;
    // Exodus-end

    public GiveBlipsEvent(
        List<BlipConfig> configPalette,
        List<BlipNetData> blips,
        List<HitscanNetData> hitscans,
        int? requestedMapId = null, // Exodus nebula-ftl-map
        bool nebulaOnly = false) // Exodus nebula-ftl-map
    {
        ConfigPalette = configPalette;
        Blips = blips;
        HitscanLines = hitscans;
        RequestedMapId = requestedMapId; // Exodus nebula-ftl-map
        NebulaOnly = nebulaOnly; // Exodus nebula-ftl-map
    }
}

[Serializable, NetSerializable]
public sealed class RequestBlipsEvent : EntityEventArgs
{
    public NetEntity Radar;
    // Exodus-begin nebula-ftl-map
    public int? RequestedMapId;
    public bool NebulaOnly;
    // Exodus-end

    public RequestBlipsEvent(NetEntity radar, int? requestedMapId = null, bool nebulaOnly = false) // Exodus nebula-ftl-map
    {
        Radar = radar;
        RequestedMapId = requestedMapId; // Exodus nebula-ftl-map
        NebulaOnly = nebulaOnly; // Exodus nebula-ftl-map
    }
}

[Serializable, NetSerializable]
public sealed class BlipRemovalEvent : EntityEventArgs
{
    public NetEntity NetBlipUid { get; set; }

    public BlipRemovalEvent(NetEntity netBlipUid)
    {
        NetBlipUid = netBlipUid;
    }
}

[Serializable, NetSerializable]
public record struct BlipNetData
(
    NetEntity Uid,
    NetCoordinates Position,
    Vector2 Vel,
    Angle Rotation,
    ushort ConfigIndex,
    ushort? OnGridConfigIndex
);

[Serializable, NetSerializable]
public record struct HitscanNetData(Vector2 Start, Vector2 End, float Thickness, Color Color);

[Serializable, NetSerializable, DataDefinition]
public partial record struct BlipConfig
{
    [DataField]
    public Box2 Bounds = new Box2(-0.5f, -0.5f, 0.5f, 0.5f);

    [DataField]
    public Color Color = Color.OrangeRed;

    [DataField]
    public RadarBlipShape Shape = RadarBlipShape.Circle;

    // Exodus-begin nebula-radar-visualization
    /// <summary>
    /// Optional local-space polygon points for blip shapes that need a custom outline.
    /// </summary>
    [DataField]
    public List<Vector2>? Points = null;
    // Exodus-end
    [DataField]
    public bool RespectZoom = false;

    [DataField]
    public bool Rotate = false;

    public BlipConfig() { }
}
