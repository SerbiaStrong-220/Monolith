using Content.Shared._Exodus.NPC.Pet;
using Content.Shared.Damage;
using Content.Shared.Mobs.Components;
using Content.Shared.NPC.Components;
using Content.Shared.NPC.Systems;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.NPC.Pet;

public sealed class PetWarriorSystem : EntitySystem
{
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private IGameTiming _timing = default!;

    // Reused each update so don't allocate when cut expired aggro targets.
    private readonly List<EntityUid> _expired = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PetWarriorComponent, BeforeDamageChangedEvent>(OnPetDamage);
        SubscribeLocalEvent<PetMasterComponent, DamageChangedEvent>(OnMasterDamaged);
        SubscribeLocalEvent<MeleeWeaponComponent, MeleeHitEvent>(OnMeleeHit);
        SubscribeLocalEvent<ProjectileComponent, ProjectileHitEvent>(OnProjectileHit);
    }

    private void OnPetDamage(Entity<PetWarriorComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (!ent.Comp.ImmuneToMaster)
            return;

        if (TryComp<PetComponent>(ent, out var pet) && pet.Master == args.Origin)
            args.Cancelled = true;
    }

    private void OnMasterDamaged(Entity<PetMasterComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.Origin is not { } origin)
            return;

        AggroPets(ent, origin);
    }

    private void OnMeleeHit(Entity<MeleeWeaponComponent> ent, ref MeleeHitEvent args)
    {
        if (!TryComp<PetMasterComponent>(args.User, out var master))
            return;

        foreach (var target in args.HitEntities)
            AggroPets((args.User, master), target);
    }

    private void OnProjectileHit(Entity<ProjectileComponent> ent, ref ProjectileHitEvent args)
    {
        if (args.Shooter is not { } shooter || !TryComp<PetMasterComponent>(shooter, out var master))
            return;

        AggroPets((shooter, master), args.Target);
    }

    // Mark target hostile for warrior pet.
    private void AggroPets(Entity<PetMasterComponent> master, EntityUid target)
    {
        // Never aggro on own master, other pets, or non-creatures.
        if (target == master.Owner || master.Comp.Pets.Contains(target) || !HasComp<MobStateComponent>(target))
            return;

        var now = _timing.CurTime;
        foreach (var pet in master.Comp.Pets)
        {
            if (!TryComp<PetWarriorComponent>(pet, out var warrior))
                continue;

            _faction.AggroEntity(pet, target);
            warrior.AggroMemories[target] = now + warrior.AggroMemory;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<PetWarriorComponent, FactionExceptionComponent>();
        while (query.MoveNext(out var uid, out var warrior, out var exception))
        {
            if (warrior.AggroMemories.Count == 0)
                continue;

            _expired.Clear();
            foreach (var (target, forgetAt) in warrior.AggroMemories)
            {
                if (!TerminatingOrDeleted(target) && now < forgetAt)
                    continue;

                _expired.Add(target);
            }

            foreach (var target in _expired)
            {
                _faction.DeAggroEntity((uid, exception), target);
                warrior.AggroMemories.Remove(target);
            }
        }
    }
}
