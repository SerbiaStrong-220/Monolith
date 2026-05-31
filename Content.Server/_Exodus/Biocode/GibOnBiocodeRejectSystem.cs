using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Inventory;

namespace Content.Server._Exodus.Biocode;

public sealed class GibOnBiocodeRejectSystem : EntitySystem
{
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GibOnBiocodeRejectComponent, BiocodeRejectedEvent>(OnBiocodeRejected);
    }

    private void OnBiocodeRejected(Entity<GibOnBiocodeRejectComponent> ent, ref BiocodeRejectedEvent args)
    {
        if (Deleted(args.User))
            return;

        if (ent.Comp.DeleteItems)
        {
            foreach (var item in _inventory.GetHandOrInventoryEntities(args.User))
            {
                Del(item);
            }
        }

        if (ent.Comp.DeleteOrgans && TryComp<BodyComponent>(args.User, out var body))
        {
            foreach (var organ in _body.GetBodyOrganEntityComps<TransformComponent>((args.User, body)))
            {
                Del(organ.Owner);
            }
        }

        if (ent.Comp.Gib)
            _body.GibBody(args.User, true);

        args.Handled = true;
    }
}
