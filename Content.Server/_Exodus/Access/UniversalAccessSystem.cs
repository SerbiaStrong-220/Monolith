using Content.Shared._Exodus.Access;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Access;

public sealed partial class UniversalAccessSystem : EntitySystem
{
    [Dependency] private SharedAccessSystem _access = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<UniversalAccessComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<UniversalAccessComponent> ent, ref MapInitEvent args)
    {
        var access = EnsureComp<AccessComponent>(ent.Owner);
        var tags = new HashSet<ProtoId<AccessLevelPrototype>>(access.Tags);
        foreach (var prototype in _prototypes.EnumeratePrototypes<AccessLevelPrototype>())
        {
            tags.Add(prototype.ID);
        }

        _access.TrySetTags(ent.Owner, tags, access);
    }
}
