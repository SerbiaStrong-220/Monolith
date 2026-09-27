using Content.Client.Items;
using Content.Shared._Exodus.Weapons.Melee;

namespace Content.Client._Exodus.Weapons;

public sealed class MeleeChargeStatusSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        Subs.ItemStatus<MeleeChargeComponent>(ent => new MeleeChargeStatusControl(ent, EntityManager));
    }
}
