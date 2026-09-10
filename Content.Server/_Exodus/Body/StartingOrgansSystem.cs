using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Body;

public sealed class StartingOrgansSystem : EntitySystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StartingOrgansComponent, MapInitEvent>(OnMapInit, after: [typeof(SharedBodySystem)]);
    }

    private void OnMapInit(Entity<StartingOrgansComponent> ent, ref MapInitEvent args)
    {
        foreach (var (slotId, prototype) in ent.Comp.Organs)
        {
            foreach (var (partUid, part) in _body.GetBodyChildren(ent.Owner))
            {
                if (!part.Organs.ContainsKey(slotId))
                    continue;

                TryInstallOrgan((partUid, part), slotId, prototype);
                break;
            }
        }
    }

    private bool TryInstallOrgan(Entity<BodyPartComponent> part, string slotId, EntProtoId prototype)
    {
        if (!_containers.TryGetContainer(part, SharedBodySystem.GetOrganContainerId(slotId), out var container) ||
            container is not ContainerSlot slot)
        {
            return false;
        }

        var organUid = Spawn(prototype, Transform(part).Coordinates);
        if (!TryComp<OrganComponent>(organUid, out var organ) ||
            !_containers.CanInsert(organUid, slot, assumeEmpty: true))
        {
            Log.Warning($"Cannot install starting organ {prototype} in slot {slotId} of {ToPrettyString(part)}.");
            QueueDel(organUid);
            return false;
        }

        var previous = slot.ContainedEntity;
        if (previous is { } oldOrgan && !_body.RemoveOrgan(oldOrgan))
        {
            QueueDel(organUid);
            return false;
        }

        if (!_body.InsertOrgan(part, organUid, slotId, part.Comp, organ))
        {
            if (previous is { } restore && !_body.InsertOrgan(part, restore, slotId, part.Comp))
                Log.Error($"Could not restore organ {ToPrettyString(restore)} in slot {slotId} of {ToPrettyString(part)}.");

            QueueDel(organUid);
            return false;
        }

        if (previous is { } replaced)
            QueueDel(replaced);

        return true;
    }
}
