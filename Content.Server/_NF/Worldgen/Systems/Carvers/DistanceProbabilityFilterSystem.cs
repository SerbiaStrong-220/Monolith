using System.Linq;
using System.Numerics;
using Content.Server._NF.Worldgen.Components.Carvers;
using Content.Server.Worldgen.Systems.Debris;
using Robust.Shared.Random;

namespace Content.Server._NF.Worldgen.Systems.Carvers;

/// <summary>
/// This carves out holes in world gen based on distance from a set of known points.
/// </summary>
public sealed partial class PointSetDistanceCarverSystem : EntitySystem
{
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IRobustRandom _random = default!;
    // Cache points for lookup

    /// <inheritdoc />
    public override void Initialize()
    {
        SubscribeLocalEvent<WorldGenDistanceCarverComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<PointSetDistanceCarverComponent, PrePlaceDebrisFeatureEvent>(OnPrePlaceDebris);
    }

    private void OnInit(Entity<WorldGenDistanceCarverComponent> ent,
        ref ComponentInit args)
    {
        ent.Comp.SquaredDistanceThresholds = ent.Comp.DistanceThresholds
        .OrderByDescending(x => x.MaxDistance)
        .Select(x => new WorldGenDistanceThreshold { MaxDistance = x.MaxDistance * x.MaxDistance, Prob = x.Prob })
        .ToList();
    }

    private void OnPrePlaceDebris(EntityUid uid, PointSetDistanceCarverComponent component,
        ref PrePlaceDebrisFeatureEvent args)
    {
        // Frontier: something handled this, nothing to do
        if (args.Handled)
            return;
        // End Frontier

        var coords = _transform.ToMapCoordinates(args.Coords);

        var prob = 1.0f;
        var query = EntityQueryEnumerator<WorldGenDistanceCarverComponent, TransformComponent>();
        while (query.MoveNext(out _, out var carver, out var xform))
        {
            // Exodus-begin: carvers only affect debris on their own map.
            var carverCoords = _transform.ToMapCoordinates(xform.Coordinates);
            if (carverCoords.MapId != coords.MapId)
                continue;

            var distanceSquared = Vector2.DistanceSquared(carverCoords.Position, coords.Position);
            // Exodus-end
            float? newProb = null;
            foreach (var threshold in carver.SquaredDistanceThresholds)
            {
                if (distanceSquared > threshold.MaxDistance)
                    break;

                newProb = threshold.Prob;
            }
            if (newProb != null)
                prob = float.Min(prob, newProb.Value);
        }

        if (!_random.Prob(prob))
            args.Handled = true;
    }
}

