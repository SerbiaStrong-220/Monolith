using Content.Server.NPC.Components;
using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Mobs;
using Content.Shared.Weapons.Melee.Events;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class VirusLifecycleSystem
{
    private void InitializeOffspring()
    {
        SubscribeLocalEvent<VirusOffspringComponent, MapInitEvent>(OnOffspringInit);
        SubscribeLocalEvent<VirusOffspringComponent, MobStateChangedEvent>(OnOffspringDeath);
        SubscribeLocalEvent<VirusOffspringComponent, MeleeHitEvent>(OnMeleeHit);
    }

    private void OnOffspringInit(Entity<VirusOffspringComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Strain == null && ent.Comp.InitialVirus is { } virus)
            ent.Comp.Strain = _virology.BuildDescriptor(virus);
        ent.Comp.ExpiresAt = _timing.CurTime + ent.Comp.Lifetime;
    }

    private void OnOffspringDeath(Entity<VirusOffspringComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
            ent.Comp.Finished = true;
    }

    private void OnMeleeHit(Entity<VirusOffspringComponent> ent, ref MeleeHitEvent args)
    {
        if (!args.IsHit || ent.Comp.Finished || ent.Comp.Strain is not { } strain)
            return;

        foreach (var target in args.HitEntities)
        {
            if (target == ent.Owner || _mobState.IsDead(target))
                continue;

            _virology.TryExpose(target, strain, VirusTransmissionVector.Contact, ent.Comp.InfectionChance);
        }

        // The native melee operator will fail and let HTN choose a fresh susceptible host.
        if (TryComp<NPCMeleeCombatComponent>(ent, out var combat)
            && !_virology.CanAcquireVirus(combat.Target, strain))
            RemCompDeferred<NPCMeleeCombatComponent>(ent);
    }

    private void UpdateOffspring(TimeSpan now)
    {
        var query = EntityQueryEnumerator<VirusOffspringComponent>();
        while (query.MoveNext(out var uid, out var offspring))
        {
            if (offspring.Finished || now < offspring.ExpiresAt || _mobState.IsDead(uid)
                || offspring.Strain == null || TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                continue;

            offspring.Finished = true;
            if (offspring.DecayEffect is { } effect && !_containers.IsEntityInContainer(uid))
            {
                var visual = Spawn(effect, Transform(uid).Coordinates);
                _transform.SetLocalRotation(visual, Transform(uid).LocalRotation);
            }
            var remains = Spawn(offspring.Remains, Transform(uid).Coordinates);
            // Replace the occupant even in a full slot; decomposition must not spill through a sealed container.
            if (_containers.TryGetContainingContainer(uid, out var container)
                && (!_containers.Remove(uid, container, reparent: false, force: true)
                    || !_containers.Insert(remains, container, force: true)))
            {
                QueueDel(remains);
                QueueDel(uid);
                continue;
            }

            var reservoir = EnsureComp<VirusReservoirComponent>(remains);
            reservoir.Strain = offspring.Strain.Clone();
            reservoir.Identity = _virology.GetIdentity(reservoir.Strain);
            QueueDel(uid);
        }
    }

    public bool TryFindTarget(Entity<VirusOffspringComponent> ent, out EntityUid target)
    {
        target = default;
        if (ent.Comp.Finished || ent.Comp.Strain is not { } strain)
            return false;

        var origin = _transform.GetMapCoordinates(ent);
        var distance = float.MaxValue;
        _nearby.Clear();
        _lookup.GetEntitiesInRange(origin, ent.Comp.SearchRange, _nearby);
        foreach (var (candidate, _) in _nearby)
        {
            if (candidate == ent.Owner || _mobState.IsDead(candidate)
                || _containers.IsEntityInContainer(candidate) || !_virology.CanAcquireVirus(candidate, strain))
                continue;

            var position = _transform.GetMapCoordinates(candidate);
            var current = (position.Position - origin.Position).LengthSquared();
            if (current >= distance || !_interaction.InRangeUnobstructed(ent.Owner, candidate, ent.Comp.SearchRange))
                continue;

            target = candidate;
            distance = current;
        }

        return target.IsValid();
    }
}
