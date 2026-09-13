using Content.Server.Fluids.EntitySystems;
using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared._Exodus.Visuals;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;
using Content.Server.NPC.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotSatedSystem
{
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private PuddleSystem _puddles = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;

    private void InitializeConsumption()
    {
        SubscribeLocalEvent<RotSatedComponent, RotSatedConsumeEvent>(OnConsumed);
        SubscribeLocalEvent<RotSatedComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
    }

    private void OnSpeed(Entity<RotSatedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Activity != RotSatedActivity.None)
            args.ModifySpeed(0f, 0f);
    }

    private bool CanConsume(Entity<RotSatedComponent> ent, EntityUid corpse)
    {
        return !TerminatingOrDeleted(corpse) && _mobs.IsDead(corpse) && HasComp<BodyComponent>(corpse)
            && !_rotQuery.HasComp(corpse) && !_containers.IsEntityInContainer(corpse)
            && _transformQuery.TryComp(corpse, out var transform) && transform.GridUid == Transform(ent).GridUid
            && transform.MapUid == Transform(ent).MapUid
            && (!TryComp<RotCorpseClaimComponent>(corpse, out var claim)
                || claim.Consumer == ent.Owner || TerminatingOrDeleted(claim.Consumer));
    }

    private void FindCorpse(Entity<RotSatedComponent> ent)
    {
        _bodies.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(ent), ent.Comp.SearchRange, _bodies);
        var closest = float.MaxValue;
        foreach (var (uid, _) in _bodies)
        {
            if (!CanConsume(ent, uid) || ent.Comp.UnreachableCorpse == uid && _timing.CurTime < ent.Comp.CorpseRetryAt
                || !Transform(ent).Coordinates.TryDistance(EntityManager, Transform(uid).Coordinates, out var distance)
                || distance >= closest)
                continue;
            closest = distance;
            ent.Comp.Corpse = uid;
        }
        if (ent.Comp.Corpse is { } corpse)
            EnsureComp<RotCorpseClaimComponent>(corpse).Consumer = ent;
    }

    private void ReleaseCorpse(Entity<RotSatedComponent> ent)
    {
        if (ent.Comp.Corpse is { } corpse && TryComp<RotCorpseClaimComponent>(corpse, out var claim)
            && claim.Consumer == ent.Owner)
            RemComp<RotCorpseClaimComponent>(corpse);
        ent.Comp.Corpse = null;
    }

    /// <returns>Whether an item still needs to be removed before eating.</returns>
    private bool TryStrip(Entity<RotSatedComponent> ent, EntityUid corpse)
    {
        var slots = _inventory.GetSlotEnumerator(corpse);
        while (slots.NextItem(out _, out var definition))
        {
            // Native unequip drops dependent slots and preserves the contents of backpacks and pockets.
            _inventory.TryUnequip(ent, corpse, definition.Name, silent: true, force: true);
            return true;
        }
        if (!TryComp<HandsComponent>(corpse, out var hands))
            return false;
        foreach (var hand in hands.Hands.Values)
        {
            if (hand.HeldEntity is not { } held)
                continue;
            _hands.TryDrop(corpse, held, checkActionBlocker: false, handsComp: hands);
            return true;
        }
        return false;
    }

    private bool IsBare(EntityUid corpse)
    {
        var slots = _inventory.GetSlotEnumerator(corpse);
        if (slots.NextItem(out _))
            return false;
        if (TryComp<HandsComponent>(corpse, out var hands))
        {
            foreach (var hand in hands.Hands.Values)
            {
                if (hand.HeldEntity != null)
                    return false;
            }
        }
        return true;
    }

    public bool TryStartConsumption(Entity<RotSatedComponent> ent, EntityUid corpse)
    {
        if (ent.Comp.Activity != RotSatedActivity.None || ent.Comp.ConsumeDoAfter != null || _mobs.IsDead(ent)
            || !CanConsume(ent, corpse) || !IsBare(corpse) || _containers.IsEntityInContainer(ent)
            || !_interaction.InRangeUnobstructed(ent.Owner, corpse, 1.2f))
            return false;

        RemComp<NPCMeleeCombatComponent>(ent);
        _steering.Unregister(ent);
        var args = new DoAfterArgs(EntityManager, ent, ent.Comp.ConsumeDuration,
            new RotSatedConsumeEvent(), ent, target: corpse)
        {
            NeedHand = false,
            BreakOnMove = true,
            DistanceThreshold = 1.5f,
            MultiplyDelay = false,
        };
        if (!_doAfter.TryStartDoAfter(args, out var id))
            return false;
        ent.Comp.Corpse = corpse;
        EnsureComp<RotCorpseClaimComponent>(corpse).Consumer = ent;
        ent.Comp.ConsumeDoAfter = id;
        ent.Comp.NextSpill = _timing.CurTime;
        SetActivity(ent, RotSatedActivity.Enveloping, ent.Comp.EnvelopState, ent.Comp.EnvelopDuration);
        _audio.PlayPvs(ent.Comp.ConsumeSound, ent);
        return true;
    }

    private void OnConsumed(Entity<RotSatedComponent> ent, ref RotSatedConsumeEvent args)
    {
        if (args.Handled || ent.Comp.ConsumeDoAfter != args.DoAfter.Id)
            return;
        ent.Comp.ConsumeDoAfter = null;
        if (args.Cancelled || args.Args.Target is not { } corpse || _mobs.IsDead(ent)
            || !CanConsume(ent, corpse) || !IsBare(corpse))
        {
            CancelConsumption(ent);
            return;
        }

        args.Handled = true;
        ReleaseCorpse(ent);
        QueueDel(corpse);
        ent.Comp.PendingLarvae = ent.Comp.LarvaePerCorpse;
        SetActivity(ent, RotSatedActivity.Rising, ent.Comp.RiseState, ent.Comp.RiseDuration);
    }

    private bool UpdateConsumption(Entity<RotSatedComponent> ent)
    {
        var now = _timing.CurTime;
        switch (ent.Comp.Activity)
        {
            case RotSatedActivity.Enveloping:
            case RotSatedActivity.Consuming:
                if (ent.Comp.Corpse is not { } corpse || !CanConsume(ent, corpse) || !IsBare(corpse))
                {
                    CancelConsumption(ent);
                    return false;
                }
                if (ent.Comp.Activity == RotSatedActivity.Enveloping && now >= ent.Comp.ActivityUntil)
                    SetActivity(ent, RotSatedActivity.Consuming, ent.Comp.ConsumeState, TimeSpan.Zero);
                if (now >= ent.Comp.NextSpill)
                {
                    ent.Comp.NextSpill = now + ent.Comp.SpillInterval;
                    SpillSlurry(ent);
                }
                return true;
            case RotSatedActivity.Rising:
                if (now >= ent.Comp.ActivityUntil)
                    SetActivity(ent, RotSatedActivity.Birthing, ent.Comp.BirthState, ent.Comp.BirthDuration);
                return true;
            case RotSatedActivity.Birthing:
                if (now < ent.Comp.ActivityUntil)
                    return true;
                ent.Comp.BirthRequested = ent.Comp.PendingLarvae > 0;
                SetActivity(ent, RotSatedActivity.None, string.Empty, TimeSpan.Zero);
                return false;
            default:
                return false;
        }
    }

    private void SetActivity(Entity<RotSatedComponent> ent, RotSatedActivity activity, string state, TimeSpan duration)
    {
        ent.Comp.Activity = activity;
        ent.Comp.ActivityUntil = _timing.CurTime + duration;
        if (TerminatingOrDeleted(ent))
            return;
        _appearance.SetData(ent, CreatureActivityVisuals.State, state);
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void CancelConsumption(Entity<RotSatedComponent> ent)
    {
        var id = ent.Comp.ConsumeDoAfter;
        ent.Comp.ConsumeDoAfter = null;
        _doAfter.Cancel(id);
        ReleaseCorpse(ent);
        SetActivity(ent, RotSatedActivity.None, string.Empty, TimeSpan.Zero);
    }

    private void SpawnLarvae(Entity<RotSatedComponent> ent)
    {
        if (ent.Comp.PendingLarvae <= 0 || TerminatingOrDeleted(ent) || _mobs.IsDead(ent))
            return;
        for (var i = 0; i < ent.Comp.PendingLarvae; i++)
        {
            var coordinates = Transform(ent).Coordinates;
            var offset = coordinates.Offset(_random.NextVector2(0.5f));
            if (_interaction.InRangeUnobstructed(ent, offset, 0.8f))
                coordinates = offset;
            var child = Spawn(ent.Comp.Larva, coordinates);
            if (TryComp<RotLarvaComponent>(child, out var larva)
                && TryComp<VirusOffspringComponent>(ent, out var parent) && parent.Strain is { } strain)
                larva.Strain = VirusLifecycleSystem.FreshInfection(strain);
        }
        ent.Comp.PendingLarvae = 0;
        SpillSlurry(ent);
    }

    private void SpillSlurry(Entity<RotSatedComponent> ent)
    {
        if (!TryComp<VirusOffspringComponent>(ent, out var vector) || vector.Strain is not { } strain)
            return;
        var solution = new Solution();
        solution.AddReagent(new ReagentId(ent.Comp.SlurryReagent,
            [new VirusData { Viruses = [VirusLifecycleSystem.FreshInfection(strain)] }]), ent.Comp.SlurryAmount);
        var coordinates = Transform(ent).Coordinates;
        var offset = coordinates.Offset(_random.NextVector2(0.8f));
        if (_interaction.InRangeUnobstructed(ent, offset, 1f))
            coordinates = offset;
        _puddles.TrySpillAt(coordinates, solution, out _, sound: false);
    }
}
