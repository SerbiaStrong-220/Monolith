using System.Numerics;
using Content.Shared._Exodus.Mining.AutoMining;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningBeamOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly BulkAutoMiningEmitterVisualSystem _visuals;
    private readonly BulkAutoMiningBeamRenderer _renderer;
    private readonly BulkAutoMiningBeamRenderer _linkRenderer;

    /// <summary>Links already drawn this frame, keyed by their lower laser first.</summary>
    private readonly HashSet<(EntityUid, EntityUid)> _drawnLinks = new();

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public BulkAutoMiningBeamOverlay()
    {
        IoCManager.InjectDependencies(this);
        _visuals = _entities.System<BulkAutoMiningEmitterVisualSystem>();
        _renderer = new BulkAutoMiningBeamRenderer(_prototypes);
        _linkRenderer = new BulkAutoMiningBeamRenderer(_prototypes, BulkAutoMiningBeamRenderer.LinkShader);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Beam state belongs to the visible emitter, not a possibly out-of-PVS console.
        _drawnLinks.Clear();
        var query = _entities.EntityQueryEnumerator<BulkAutoMiningEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var emitter, out var xform))
        {
            if (xform.MapID != args.MapId)
                continue;

            var renderer = _renderer;
            var link = false;
            if (!_visuals.TryGetBeam((uid, emitter, xform), out var origin, out var target, out _))
            {
                if (!_visuals.TryGetLinkBeam((uid, emitter, xform), out origin, out target, out _))
                    continue;

                renderer = _linkRenderer;
                link = true;
            }

            var padding = new Vector2(BulkAutoMiningBeamRenderer.WorldWidth);
            var bounds = new Box2(Vector2.Min(origin, target) - padding, Vector2.Max(origin, target) + padding);
            if (!args.WorldAABB.Intersects(bounds))
                continue;

            // Either end can draw the shared link, so it stays visible when the partner laser is outside PVS.
            if (link && !_drawnLinks.Add(GetLinkKey(uid, emitter.LinkPartner!.Value)))
                continue;

            renderer.Draw(args.WorldHandle, origin, target, BulkAutoMiningBeamRenderer.WorldWidth,
                Vector2.Distance(origin, target));
        }
    }

    private static (EntityUid, EntityUid) GetLinkKey(EntityUid a, EntityUid b) => a.CompareTo(b) < 0 ? (a, b) : (b, a);
}
