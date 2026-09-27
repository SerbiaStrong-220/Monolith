using System.Numerics;
using Content.Shared._Exodus.Mining.AutoMining;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Mining.AutoMining;

public sealed class BulkAutoMiningBeamOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly BulkAutoMiningEmitterVisualSystem _visuals;
    private readonly BulkAutoMiningBeamRenderer _renderer;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public BulkAutoMiningBeamOverlay()
    {
        IoCManager.InjectDependencies(this);
        _visuals = _entities.System<BulkAutoMiningEmitterVisualSystem>();
        _renderer = new BulkAutoMiningBeamRenderer(_prototypes);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        // Beam state belongs to the visible emitter, not a possibly out-of-PVS console.
        var query = _entities.EntityQueryEnumerator<BulkAutoMiningEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var emitter, out var xform))
        {
            if (xform.MapID != args.MapId ||
                !_visuals.TryGetBeam((uid, emitter, xform), out var origin, out var target, out _))
                continue;

            var padding = new Vector2(BulkAutoMiningBeamRenderer.WorldWidth);
            var bounds = new Box2(Vector2.Min(origin, target) - padding, Vector2.Max(origin, target) + padding);
            if (!args.WorldAABB.Intersects(bounds))
                continue;

            _renderer.Draw(args.WorldHandle, origin, target, BulkAutoMiningBeamRenderer.WorldWidth,
                Vector2.Distance(origin, target));
        }
    }
}
