using Content.Shared._Exodus.Genetics;
using Content.Shared._Exodus.Medical;
using Content.Shared.Damage;
using Content.Shared.Destructible;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Medical;

public sealed partial class HealingCocoonSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedDestructibleSystem _destructible = default!;

    private EntityQuery<HealingCocoonComponent> _cocoons;
    private EntityQuery<MobStateComponent> _mobStates;

    public override void Initialize()
    {
        base.Initialize();
        _cocoons = GetEntityQuery<HealingCocoonComponent>();
        _mobStates = GetEntityQuery<MobStateComponent>();
        SubscribeLocalEvent<HealingCocoonComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<HealingCocoonComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<HealingCocoonComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<HealingCocoonComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<CocoonPatientComponent, RespirationAttemptEvent>(OnRespiration);
    }

    private void OnInserted(Entity<HealingCocoonComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != ent.Comp.ContainerId)
            return;
        _appearance.SetData(ent, HealingCocoonVisuals.Occupied, true);
        var patient = EnsureComp<CocoonPatientComponent>(args.Entity);
        patient.Cocoon = ent;
        patient.NextHeal = _timing.CurTime + ent.Comp.Interval;
    }

    private void OnRemoved(Entity<HealingCocoonComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != ent.Comp.ContainerId)
            return;
        if (TryComp<CocoonPatientComponent>(args.Entity, out var patient) && patient.Cocoon == ent.Owner)
            RemCompDeferred<CocoonPatientComponent>(args.Entity);

        // Leaving consumes the shelter. Rupture marks it before ejecting a patient,
        // so destruction-triggered removal cannot recursively destroy it again.
        if (!ent.Comp.Ruptured && !TerminatingOrDeleted(ent))
            _destructible.DestroyEntity(ent);
    }

    private bool TryGetShelter(Entity<CocoonPatientComponent> ent, out HealingCocoonComponent cocoon)
    {
        return _cocoons.TryComp(ent.Comp.Cocoon, out cocoon!) && !cocoon.Ruptured && !TerminatingOrDeleted(ent.Comp.Cocoon) &&
               _containers.TryGetContainer(ent.Comp.Cocoon, cocoon.ContainerId, out var container) && container.Contains(ent);
    }

    private void OnRespiration(Entity<CocoonPatientComponent> ent, ref RespirationAttemptEvent args)
    {
        if (_mobStates.TryComp(ent, out var state) && state.CurrentState != MobState.Dead &&
            TryGetShelter(ent, out var cocoon) && cocoon.SupportsBreathing)
            args.Cancelled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<CocoonPatientComponent, MobStateComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var patient, out var state, out var damage))
        {
            if (patient.NextHeal > _timing.CurTime)
                continue;
            if (!TryGetShelter((uid, patient), out var cocoon) || cocoon.Interval <= TimeSpan.Zero)
            {
                RemCompDeferred<CocoonPatientComponent>(uid);
                continue;
            }

            // A stalled tick must not accumulate a burst of free treatment.
            var intervals = (_timing.CurTime - patient.NextHeal).Ticks / cocoon.Interval.Ticks + 1;
            patient.NextHeal += TimeSpan.FromTicks(cocoon.Interval.Ticks * intervals);
            if (state.CurrentState != MobState.Dead && damage.TotalDamage > 0 && !TerminatingOrDeleted(uid))
            {
                // Damage modifiers may mutate the specifier; keep the configured treatment intact.
                _damage.TryChangeDamage(uid, new DamageSpecifier(cocoon.Healing), true, false, damage, origin: patient.Cocoon);
            }
        }
    }

    private void OnDestroyed(Entity<HealingCocoonComponent> ent, ref DestructionEventArgs args) => Rupture(ent);

    private void OnTerminating(Entity<HealingCocoonComponent> ent, ref EntityTerminatingEvent args) => Eject(ent);

    private void Rupture(Entity<HealingCocoonComponent> ent)
    {
        if (ent.Comp.Ruptured || TerminatingOrDeleted(ent))
            return;

        ent.Comp.Ruptured = true;
        Dirty(ent);
        Eject(ent);
        if (ent.Comp.RemainsPrototype is { } remains)
            Spawn(remains, Transform(ent).Coordinates);
    }

    private void Eject(Entity<HealingCocoonComponent> ent)
    {
        if (!_containers.TryGetContainer(ent, ent.Comp.ContainerId, out var container))
            return;

        // Direct deletion does not run EntityStorage's destruction behavior.
        // Detach the patient before the engine recursively deletes the cocoon's children.
        var parent = Transform(ent).ParentUid;
        if (parent.IsValid() && TerminatingOrDeleted(parent))
            return; // Map/grid teardown should still delete its inhabitants normally.
        _containers.EmptyContainer(container, force: true);
    }
}
