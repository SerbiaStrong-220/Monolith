using System.Numerics;
using Content.Client._Exodus.Nebula;
using Content.Client._Exodus.NPC;
using Content.Client._Mono.Radar;
using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.Detection;
using Content.Shared._Mono.Radar;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.UI.MapObjects;
using Robust.Client.Graphics;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Client.Shuttles.UI;

// Exodus helpers moved from ShuttleMapControl.xaml.cs into this partial file.
public sealed partial class ShuttleMapControl
{
    private const float NebulaFillAlpha = 0.08f;
    private const float TerritoryMediumIconThreshold = 1750f;
    private const float TerritoryLargeIconThreshold = 3750f;
    private const float TerritoryHugeIconThreshold = 4500f;

    private readonly RadarBlipsSystem _blips;
    private readonly NebulaSystem _nebula;

    private Vector2[] _nebulaFillBuffer = [];
    private Vector2[] _nebulaLineBuffer = [];

    private bool CanFTLToNebulaPreview(EntityUid shuttleUid, EntityCoordinates targetCoordinates, Angle targetAngle)
    {
        return _nebula.CanFTL(shuttleUid, targetCoordinates, targetAngle, out _);
    }

    private string AddFactionAiControlLabel(EntityUid grid, string labelText)
    {
        _factionAiControlQuery.TryGetComponent(grid, out var control);
        return FactionAiControlLabelHelper.AppendToLabel(labelText, control, PrototypeManager);
    }

    private void DrawMapObjectLabel(DrawingHandleScreen handle, Vector2 position, string text, Color color)
    {
        var remaining = text.AsSpan();
        var y = 0f;

        while (true)
        {
            var lineEnd = remaining.IndexOf('\n');
            var line = lineEnd >= 0 ? remaining[..lineEnd] : remaining;
            var dimensions = handle.GetDimensions(_font, line, 1f);
            var offset = new Vector2(-dimensions.X / 2f, y + dimensions.Y * UIScale);
            handle.DrawString(_font, position + offset, line, 1f, color);
            y += dimensions.Y * UIScale;

            if (lineEnd < 0)
                break;

            remaining = remaining[(lineEnd + 1)..];
        }
    }

    private Box2 GetControlViewBounds()
    {
        var margin = 3f * UIScale;
        return new Box2(-margin, -margin, PixelSize.X + margin, PixelSize.Y + margin);
    }

    private void DrawTerritoryRings(DrawingHandleScreen handle, List<IMapObject> mapObjects, Matrix3x2 matty, Box2 viewBounds)
    {
        foreach (var mapObj in mapObjects)
        {
            if (mapObj is not GridMapObject gridObj ||
                !_gridTerritoryQuery.TryGetComponent(gridObj.Entity, out var terrRing) ||
                terrRing.Radius <= 0f)
            {
                continue;
            }

            if (!TryGetVisibleGridMapObject(mapObj, matty, out _, out _, out var gridUiPos))
                continue;

            var ringRadius = terrRing.Radius * MinimapScale;
            if (!CircleIntersectsBox(gridUiPos, ringRadius, viewBounds))
                continue;

            var ringBase = GetTerritoryRingColor(terrRing);
            handle.DrawCircle(gridUiPos, ringRadius, ringBase.WithAlpha(0.035f));
            handle.DrawCircle(gridUiPos, ringRadius, ringBase.WithAlpha(0.28f), filled: false);
        }
    }

    private bool TryGetVisibleGridMapObject(
        IMapObject mapObj,
        Matrix3x2 matty,
        out Entity<MapGridComponent> grid,
        out Vector2 gridRelativePos,
        out Vector2 gridUiPos)
    {
        grid = default;
        gridRelativePos = default;
        gridUiPos = default;

        if (mapObj is not GridMapObject gridObj || !_mapGridQuery.TryGetComponent(gridObj.Entity, out var mapGrid))
            return false;

        if (EntManager.HasComponent<MapComponent>(gridObj.Entity) || !_entManager.EntityExists(gridObj.Entity))
            return false;

        grid = (gridObj.Entity, mapGrid);
        IFFComponent? iffComp = null;

        if (grid.Owner != _shuttleEntity &&
            _iffQuery.TryGetComponent(grid.Owner, out iffComp) &&
            (iffComp.Flags & IFFFlags.Hide) != 0x0)
        {
            return false;
        }

        var hideLabel = iffComp != null && (iffComp.Flags & IFFFlags.HideLabel) != 0x0;
        var detectionLevel = _console == null ? DetectionLevel.Detected : _detection.IsGridDetected(grid.Owner, _console.Value);
        var detected = detectionLevel != DetectionLevel.Undetected || !hideLabel;
        if (!detected)
            return false;

        if (!_physicsQuery.TryGetComponent(grid.Owner, out var gridPhysics))
            return false;

        var (gridPos, gridRot) = _xformSystem.GetWorldPositionRotation(grid.Owner);
        gridPos = Maps.GetGridPosition((grid, gridPhysics), gridPos, gridRot);

        gridRelativePos = Vector2.Transform(gridPos, matty);
        gridRelativePos = gridRelativePos with { Y = -gridRelativePos.Y };
        gridUiPos = ScalePosition(gridRelativePos);
        return true;
    }

    private Color GetTerritoryRingColor(GridTerritoryComponent terrRing)
    {
        if (terrRing.ControllingFaction is { } factionId &&
            PrototypeManager.TryIndex(factionId, out var factionProto))
        {
            return factionProto.Color;
        }

        return new Color(0.65f, 0.65f, 0.65f);
    }

    private static bool CircleIntersectsBox(Vector2 center, float radius, Box2 box)
    {
        var closest = new Vector2(
            Math.Clamp(center.X, box.Left, box.Right),
            Math.Clamp(center.Y, box.Bottom, box.Top));

        return (closest - center).LengthSquared() <= radius * radius;
    }

    private ValueList<Vector2> GetTerritoryMapObject(Vector2 localPos, Angle angle, float territoryRadius, float scale = 1f, bool scalePosition = false)
    {
        var sides = GetTerritoryMapSides(territoryRadius);
        var baseRadius = GetMapObjectRadius();
        var vertexRadius = 2 * baseRadius * scale;
        var points = new ValueList<Vector2>(sides);

        var startAng = sides switch
        {
            3 => MathF.PI / 2,
            4 => MathF.PI / 4,
            _ => -MathF.PI / 2
        };

        var angleStep = 2 * MathF.PI / sides;
        for (var i = 0; i < sides; i++)
        {
            var a = startAng + i * angleStep;
            var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
            points.Add(localPos + angle.RotateVec(dir * vertexRadius));
        }

        if (scalePosition)
        {
            for (var i = 0; i < points.Count; i++)
            {
                points[i] = ScalePosition(points[i]);
            }
        }

        return points;
    }

    private static int GetTerritoryMapSides(float territoryRadius)
    {
        if (territoryRadius < TerritoryMediumIconThreshold)
            return 3;

        if (territoryRadius < TerritoryLargeIconThreshold)
            return 4;

        if (territoryRadius < TerritoryHugeIconThreshold)
            return 5;

        return 6;
    }

    private void AddMapPolygon(List<Vector2> edges, List<Vector2> verts, ValueList<Vector2> points)
    {
        if (points.Count < 3)
            return;

        for (var i = 1; i < points.Count - 1; i++)
        {
            verts.Add(points[0]);
            verts.Add(points[i]);
            verts.Add(points[i + 1]);
        }

        for (var i = 0; i < points.Count; i++)
        {
            edges.Add(points[i]);
            edges.Add(points[(i + 1) % points.Count]);
        }
    }

    private void DrawNebulaBlips(DrawingHandleScreen handle, Matrix3x2 mapTransform)
    {
        if (_console != null)
            _blips.RequestNebulaMapBlips(_console.Value, ViewingMap);

        var blips = _blips.GetCurrentNebulaMapBlips();
        foreach (var blip in blips)
        {
            if (blip.Config.Shape != RadarBlipShape.NebulaPolygon ||
                blip.Config.Points == null ||
                blip.Config.Points.Count < 3)
            {
                continue;
            }

            var mapCoords = _xformSystem.ToMapCoordinates(blip.Position);
            if (mapCoords.MapId != ViewingMap)
                continue;

            var relativePos = Vector2.Transform(mapCoords.Position, mapTransform);
            var uiPosition = ScalePosition(relativePos with { Y = -relativePos.Y });
            DrawNebulaPolygon(handle, uiPosition, blip.Config);
        }
    }

    private void DrawNebulaPolygon(DrawingHandleScreen handle, Vector2 position, BlipConfig config)
    {
        if (config.Points == null || config.Points.Count < 3)
            return;

        if (config.InvertFill && config.OuterFillRadius > 0f)
        {
            var n = config.Points.Count;
            var ringCount = 2 * (n + 1);
            if (_nebulaFillBuffer.Length < ringCount)
                _nebulaFillBuffer = new Vector2[ringCount];

            for (var i = 0; i <= n; i++)
            {
                var k = i % n;
                var theta = MathF.Tau * k / n;
                var outerPoint = new Vector2(config.OuterFillRadius * MathF.Cos(theta), config.OuterFillRadius * MathF.Sin(theta)); // Exodus mass-scanner-fix: keep invert-fill outer radius stable in world space.
                var innerPoint = config.Points[k];

                if (config.RespectZoom)
                {
                    outerPoint *= MinimapScale;
                    innerPoint *= MinimapScale;
                }

                _nebulaFillBuffer[2 * i] = position + (outerPoint with { Y = -outerPoint.Y });
                _nebulaFillBuffer[2 * i + 1] = position + (innerPoint with { Y = -innerPoint.Y });
            }

            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleStrip, new Span<Vector2>(_nebulaFillBuffer, 0, ringCount), config.Color.WithAlpha(NebulaFillAlpha));
        }
        else if (!config.InvertFill)
        {
            var fillCount = config.Points.Count + 2;
            if (_nebulaFillBuffer.Length < fillCount)
                _nebulaFillBuffer = new Vector2[fillCount];

            _nebulaFillBuffer[0] = position;

            for (var i = 0; i < config.Points.Count; i++)
            {
                var point = config.Points[i];
                if (config.RespectZoom)
                    point *= MinimapScale;

                _nebulaFillBuffer[i + 1] = position + (point with { Y = -point.Y });
            }

            _nebulaFillBuffer[fillCount - 1] = _nebulaFillBuffer[1];
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, new Span<Vector2>(_nebulaFillBuffer, 0, fillCount), config.Color.WithAlpha(NebulaFillAlpha));
        }

        var lineCount = config.Points.Count + 1;
        if (_nebulaLineBuffer.Length < lineCount)
            _nebulaLineBuffer = new Vector2[lineCount];

        for (var i = 0; i < config.Points.Count; i++)
        {
            var point = config.Points[i];
            if (config.RespectZoom)
                point *= MinimapScale;

            _nebulaLineBuffer[i] = position + (point with { Y = -point.Y });
        }

        _nebulaLineBuffer[lineCount - 1] = _nebulaLineBuffer[0];
        handle.DrawPrimitives(DrawPrimitiveTopology.LineStrip, new Span<Vector2>(_nebulaLineBuffer, 0, lineCount), config.Color.WithAlpha(0.9f));
    }
}
