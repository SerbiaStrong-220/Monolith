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
    NebulaPolygon, // Exodus nebula-radar-visualization
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

    public readonly int? RequestedMapId; // Exodus nebula-ftl-map
    public readonly bool NebulaOnly; // Exodus nebula-ftl-map

    public GiveBlipsEvent(
        List<BlipConfig> configPalette,
        List<BlipNetData> blips,
        List<HitscanNetData> hitscans,
        int? requestedMapId = null,
        bool nebulaOnly = false)
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
    public int? RequestedMapId; // Exodus nebula-ftl-map
    public bool NebulaOnly; // Exodus nebula-ftl-map

    public RequestBlipsEvent(NetEntity radar, int? requestedMapId = null, bool nebulaOnly = false)
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

    /// <summary>
    /// When true, <see cref="Points"/> defines the inner boundary; fill is drawn as a
    /// ring between that boundary and <see cref="OuterFillRadius"/> (world-space tiles).
    /// </summary>
    [DataField]
    public bool InvertFill = false;

    /// <summary>
    /// World-space outer radius for ring fill when <see cref="InvertFill"/> is true.
    /// </summary>
    [DataField]
    public float OuterFillRadius = 0f;
    // Exodus-end

    // Exodus-begin territory-marker
    /// <summary>
    /// Optional outline color for blips that draw a filled shape and a separate border.
    /// </summary>
    [DataField]
    public Color BorderColor = Color.Transparent;

    /// <summary>
    /// Optional localization key for a text label repeated across the territory zone.
    /// </summary>
    [DataField]
    public string? Label = null;
    // Exodus-end

    [DataField]
    public bool RespectZoom = false;

    [DataField]
    public bool Rotate = false;

    public BlipConfig() { }
}
