// SS220 / Exodus: physiology API used by configurable breathing inversion.
using Content.Server.Body.Components;
using Content.Shared.Body.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Body.Systems;

public sealed partial class MetabolizerSystem
{
    public void SetMetabolizerTypes(Entity<MetabolizerComponent> entity, HashSet<ProtoId<MetabolizerTypePrototype>> types)
    {
        entity.Comp.MetabolizerTypes = types;
    }
}
