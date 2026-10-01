using System.Numerics;
using Content.Client._Exodus.Shuttles.UI;
using Content.Shared._Mono.Radar;
using Robust.Client.Graphics;
using RadarBlipData = Content.Client._Mono.Radar.BlipData;

namespace Content.Client.Shuttles.UI;

// Exodus: mass scanners show bluespace suppression fields, fainter than on the FTL map.
public partial class ShuttleNavControl
{
    private const float SuppressionHatchSpacing = 9f;
    private const float SuppressionHatchAlpha = 0.16f;
    private const float SuppressionOutlineAlpha = 0.35f;

    private readonly HatchedCircleRenderer _suppressionHatch = new();

    private void DrawSuppressionFields(DrawingHandleScreen handle, Matrix3x2 worldToView, List<RadarBlipData> blips)
    {
        // The scanner shows its whole square, so the field is drawn up to the control edges like territory circles.
        var viewBounds = new Box2(Vector2.Zero, PixelSize);
        foreach (var blip in blips)
        {
            if (blip.Config.Shape != RadarBlipShape.SuppressionField)
                continue;

            var radius = GetTerritoryScreenRadius(blip.Config);
            if (radius <= 0f)
                continue;

            var center = Vector2.Transform(_transform.ToMapCoordinates(blip.Position).Position, worldToView);
            _suppressionHatch.DrawHatch(handle, center, radius, SuppressionHatchSpacing * UIScale,
                blip.Config.Color.WithAlpha(SuppressionHatchAlpha), viewBounds);
            _suppressionHatch.DrawOutline(handle, center, radius,
                blip.Config.BorderColor.WithAlpha(SuppressionOutlineAlpha), viewBounds);
        }
    }
}
