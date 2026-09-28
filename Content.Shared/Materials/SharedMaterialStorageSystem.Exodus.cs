using Content.Shared._Exodus.Materials; // Exodus

namespace Content.Shared.Materials;

// Exodus-begin: allow machine upgrades to resize material buffers without changing their contents.
public abstract partial class SharedMaterialStorageSystem
{
    /// <summary>
    /// Sets the local capacity. Existing contents are preserved; overfilled buffers can still be consumed.
    /// Null means unlimited capacity.
    /// </summary>
    public void SetStorageLimit(Entity<MaterialStorageComponent> ent, int? limit)
    {
        limit = limit is { } value ? Math.Max(0, value) : null;
        if (ent.Comp.StorageLimit == limit)
            return;

        ent.Comp.StorageLimit = limit;
        var ev = new MaterialStorageCapacityChangedEvent();
        RaiseLocalEvent(ent, ref ev);
    }
}
// Exodus-end
