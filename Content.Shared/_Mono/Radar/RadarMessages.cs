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

    public GiveBlipsEvent(
        List<BlipConfig> configPalette,
        List<BlipNetData> blips,
        List<HitscanNetData> hitscans)
    {
        ConfigPalette = configPalette;
        Blips = blips;
        HitscanLines = hitscans;
    }
}

[Serializable, NetSerializable]
public sealed class RequestBlipsEvent : EntityEventArgs
{
    public NetEntity Radar;
    public RequestBlipsEvent(NetEntity radar)
    {
        Radar = radar;
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

    /// <summary>
    /// Optional unscaled screen-space offset for the repeated label.
    /// Applied after centering, so the label still rotates around the blip pivot.
    /// </summary>
    [DataField]
    public Vector2 LabelOffset = Vector2.Zero;
    // Exodus-end

    [DataField]
    public bool RespectZoom = false;

    [DataField]
    public bool Rotate = false;

    public BlipConfig() { }
}
