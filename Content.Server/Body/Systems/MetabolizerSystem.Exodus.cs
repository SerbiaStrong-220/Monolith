// SS220 / Exodus: physiology API used by configurable breathing inversion.
using Content.Server.Body.Components;
using Content.Shared.Body.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Body.Systems;

public sealed partial class MetabolizerSystem
{
    // Exodus-begin: explicit organ metabolism through the owning system.
    /// <summary>Processes one metabolism cycle for this organ without changing its periodic schedule.</summary>
    public void Metabolize(Entity<MetabolizerComponent> entity)
    {
        if (TerminatingOrDeleted(entity) || entity.Comp.Deleted)
            return;

        TryMetabolize((entity.Owner, entity.Comp));
    }
    // Exodus-end

    public void SetMetabolizerTypes(Entity<MetabolizerComponent> entity, HashSet<ProtoId<MetabolizerTypePrototype>>? types) // Exodus: restore untyped organs after adaptation.
    {
        entity.Comp.MetabolizerTypes = types;
    }
}
