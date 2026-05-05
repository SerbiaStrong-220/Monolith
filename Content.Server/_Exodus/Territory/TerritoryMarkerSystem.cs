using System.Numerics;
using Content.Server._Mono.Radar;
using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.Radar;
using Robust.Shared.Physics.Components;

namespace Content.Server._Exodus.Territory;

public sealed class TerritoryMarkerSystem : EntitySystem
{
    private const float MaxRadarDistance = 300_000f;
    private const int CircleSamples = 96;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TerritoryMarkerComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<TerritoryMarkerComponent> ent, ref ComponentStartup args)
    {
        EnsureComp<PhysicsComponent>(ent);

        var blip = EnsureComp<RadarBlipComponent>(ent);
        blip.MaxDistance = MaxRadarDistance;
        blip.RequireNoGrid = false;
        blip.VisibleFromOtherGrids = true;
        blip.Config = new BlipConfig
        {
            Bounds = new Box2(-ent.Comp.Radius, -ent.Comp.Radius, ent.Comp.Radius, ent.Comp.Radius),
            Color = ent.Comp.FillColor,
            Shape = RadarBlipShape.TerritoryCircle,
            Points = BuildCirclePoints(ent.Comp.Radius),
            RespectZoom = true,
            Rotate = false,
        };
    }

    private static List<Vector2> BuildCirclePoints(float radius)
    {
        var points = new List<Vector2>(CircleSamples);
        for (var i = 0; i < CircleSamples; i++)
        {
            var theta = MathF.Tau * i / CircleSamples;
            points.Add(new Vector2(MathF.Cos(theta) * radius, MathF.Sin(theta) * radius));
        }
        return points;
    }
}
