using System.Numerics;
using Content.Client._Exodus.Mining.AutoMining;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using RadarBlipData = Content.Client._Mono.Radar.BlipData;

namespace Content.Client.Shuttles.UI;

// Exodus: all mass scanners draw mining beams from radar data, independent of emitter PVS visibility.
public partial class ShuttleNavControl
{
    private BulkAutoMiningEmitterVisualSystem? _miningVisuals;
    private BulkAutoMiningBeamRenderer? _miningBeamRenderer;

    protected virtual void DrawAdditionalOverlays(DrawingHandleScreen handle, Matrix3x2 worldToView, MapId mapId,
        List<RadarBlipData> blips, EntityUid? ownGrid)
    {
        _miningVisuals ??= EntManager.System<BulkAutoMiningEmitterVisualSystem>();
        _miningBeamRenderer ??= new BulkAutoMiningBeamRenderer(_prototype);
        foreach (var blip in blips)
        {
            if (blip.MiningBeam is not { } beam ||
                !_miningVisuals.TryGetRadarBeam(blip.Position, beam, mapId, out var origin, out var target))
                continue;

            if (blip.GridUid is { } grid && grid != ownGrid && !_visibleGridsSet.Contains(grid))
                continue;

            var screenOrigin = Vector2.Transform(origin, worldToView);
            var screenTarget = Vector2.Transform(target, worldToView);
            var worldLength = Vector2.Distance(origin, target);
            var screenLength = Vector2.Distance(screenOrigin, screenTarget);
            if (worldLength < 0.01f)
                continue;

            var width = Math.Clamp(BulkAutoMiningBeamRenderer.WorldWidth * screenLength / worldLength,
                8f * UIScale, 14f * UIScale);
            // Keep animated bands readable when a long beam is compressed into a small radar view.
            var waveDistance = Math.Min(worldLength, screenLength / (4f * UIScale));
            _miningBeamRenderer.Draw(handle, screenOrigin, screenTarget, width, waveDistance);
        }
    }
}
