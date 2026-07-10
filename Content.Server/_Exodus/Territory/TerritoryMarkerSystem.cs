using Content.Server._Mono.Radar;
using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.Radar;
using Robust.Shared.Physics.Components;

namespace Content.Server._Exodus.Territory;

public sealed partial class TerritoryMarkerSystem : EntitySystem
{
    private const float RadarEdgeVisibilityPadding = 10_000f;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TerritoryMarkerComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<TerritoryMarkerComponent> ent, ref ComponentStartup args)
    {
        SyncBlip(ent);
    }

    /// <summary>
    /// Re-applies the current visual configuration from the TerritoryMarkerComponent
    /// to the associated RadarBlipComponent (TerritoryCircle shape).
    /// Called on startup and whenever the control layer updates radius or label
    /// (e.g. when a banner claim changes the displayed faction name or size).
    /// </summary>
    public void SyncBlip(Entity<TerritoryMarkerComponent> ent)
    {
        EnsureComp<PhysicsComponent>(ent);

        var blip = EnsureComp<RadarBlipComponent>(ent);
        blip.MaxDistance = ent.Comp.Radius + RadarEdgeVisibilityPadding;
        blip.RequireNoGrid = false;
        blip.VisibleFromOtherGrids = true;

        // Assign a fresh BlipConfig so the radar palette (value equality) picks up label/radius changes on next request.
        blip.Config = new BlipConfig
        {
            Bounds = new Box2(-ent.Comp.Radius, -ent.Comp.Radius, ent.Comp.Radius, ent.Comp.Radius),
            Color = ent.Comp.FillColor,
            BorderColor = ent.Comp.BorderColor,
            Shape = RadarBlipShape.TerritoryCircle,
            RespectZoom = true,
            Rotate = false,
            Label = ent.Comp.Text,
        };
    }
}
