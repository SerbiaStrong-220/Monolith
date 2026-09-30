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
    private BulkAutoMiningBeamRenderer? _miningLinkRenderer;

    /// <summary>Consortium links already drawn this frame, keyed by their lower laser first.</summary>
    private readonly HashSet<(NetEntity, NetEntity)> _drawnMiningLinks = new();

    protected virtual void DrawAdditionalOverlays(DrawingHandleScreen handle, Matrix3x2 worldToView, MapId mapId,
        List<RadarBlipData> blips, EntityUid? ownGrid)
    {
        _miningVisuals ??= EntManager.System<BulkAutoMiningEmitterVisualSystem>();
        _miningBeamRenderer ??= new BulkAutoMiningBeamRenderer(_prototype);
        _miningLinkRenderer ??= new BulkAutoMiningBeamRenderer(_prototype, BulkAutoMiningBeamRenderer.LinkShader);
        _drawnMiningLinks.Clear();
        foreach (var blip in blips)
        {
            if (blip.MiningBeam is not { } beam)
                continue;

            Vector2 origin;
            Vector2 target;
            var renderer = _miningBeamRenderer;
            if (beam.LinkPartner is not null)
            {
                if (!_miningVisuals.TryGetRadarLinkBeam(blip.Position, beam, mapId, out origin, out target))
                    continue;

                renderer = _miningLinkRenderer;
            }
            else if (!_miningVisuals.TryGetRadarBeam(blip.Position, beam, mapId, out origin, out target))
            {
                continue;
            }

            if (blip.GridUid is { } grid && grid != ownGrid && !_visibleGridsSet.Contains(grid))
                continue;

            // Both linked lasers report the link; the first end that is actually drawn draws it once.
            if (beam.LinkPartner is { } partner && !_drawnMiningLinks.Add(partner.CompareTo(blip.NetUid) < 0
                    ? (partner, blip.NetUid)
                    : (blip.NetUid, partner)))
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
            renderer.Draw(handle, screenOrigin, screenTarget, width, waveDistance);
        }
    }
}
