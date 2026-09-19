using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;
using Robust.Shared.Spawners;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    private void StartConstructionEffects(Entity<ShipRepairDroneComponent> ent,
        Entity<ShipRepairToolComponent> tool, ShipRepairPlan plan, TimeSpan duration)
    {
        ClearConstructionEffects(ent);
        foreach (var work in plan.Work)
        {
            // Server entities are visible to all nearby clients, without holding a handheld SRD.
            var effect = Spawn(tool.Comp.ConstructEffect, new EntityCoordinates(plan.Grid, work.Position));
            Transform(effect).GridTraversal = false;
            _transform.SetCoordinates(effect, new EntityCoordinates(plan.Grid, work.Position));
            _transform.SetLocalRotation(effect, work.Rotation);
            var visuals = EnsureComp<ShipRepairConstructionVisualsComponent>(effect);
            visuals.TargetPrototype = work.Prototype;
            visuals.TileType = work.TileType;
            Dirty(effect, visuals);
            EnsureComp<TimedDespawnComponent>(effect).Lifetime = (float) duration.TotalSeconds + 1f;
            ent.Comp.ConstructionEffects.Add(effect);
        }
    }

    private void ClearConstructionEffects(Entity<ShipRepairDroneComponent> ent)
    {
        foreach (var effect in ent.Comp.ConstructionEffects)
            TryQueueDel(effect);
        ent.Comp.ConstructionEffects.Clear();
    }
}
