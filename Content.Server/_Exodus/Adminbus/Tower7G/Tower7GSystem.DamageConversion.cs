using Content.Shared._Exodus.Adminbus.Tower7G;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Adminbus.Tower7G;

public sealed partial class Tower7GSystem
{
    private void OnConversionMapInit(Entity<Tower7GDamageConversionComponent> ent, ref MapInitEvent args)
    {
        UpdateDamageConversion((ent.Owner, ent.Comp, Transform(ent)));
    }

    private void UpdateDamageConversion()
    {
        var query = EntityQueryEnumerator<Tower7GDamageConversionComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var conversion, out var xform))
        {
            UpdateDamageConversion((uid, conversion, xform));
        }
    }

    private void UpdateDamageConversion(Entity<Tower7GDamageConversionComponent, TransformComponent> ent)
    {
        var active = IsInTowerRange((ent.Owner, ent.Comp2));
        if (ent.Comp1.Active == active)
            return;

        ent.Comp1.Active = active;
        Dirty(ent.Owner, ent.Comp1);
    }

    private bool IsInTowerRange(Entity<TransformComponent> target)
    {
        if (target.Comp.MapID == MapId.Nullspace)
            return false;

        var position = _transform.GetWorldPosition(target.Comp);
        var query = EntityQueryEnumerator<Tower7GComponent, TransformComponent, MetaDataComponent>();
        while (query.MoveNext(out var uid, out var tower, out var xform, out var metadata))
        {
            if (xform.MapID != target.Comp.MapID || tower.Range <= 0 || !float.IsFinite(tower.Range) ||
                !metadata.EntityInitialized || TerminatingOrDeleted(uid, metadata) ||
                tower.LifeStage >= ComponentLifeStage.Stopping || EntityManager.IsQueuedForDeletion(uid))
            {
                continue;
            }

            var delta = position - _transform.GetWorldPosition(xform);
            if (delta.LengthSquared() <= tower.Range * tower.Range)
                return true;
        }

        return false;
    }
}
