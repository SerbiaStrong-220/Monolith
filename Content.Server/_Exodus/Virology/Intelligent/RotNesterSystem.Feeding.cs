using System.Numerics;
using Content.Server._Exodus.Virology.Lifecycle;
using Content.Server.NPC.Components;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared._Exodus.Visuals;
using Content.Shared.DoAfter;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotNesterSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private RotNestSystem _nests = default!;
    private readonly HashSet<Entity<RotLarvaComponent>> _larvae = [];
    private readonly HashSet<Entity<RotNutritionBlobComponent>> _food = [];

    private void InitializeFeeding()
    {
        SubscribeLocalEvent<RotNesterComponent, RotNesterVomitEvent>(OnVomited);
        SubscribeLocalEvent<RotNesterComponent, RotNesterBiteEvent>(OnBite);
    }

    private bool IsHungryAlly(EntityUid nester, EntityUid larva)
    {
        if (TerminatingOrDeleted(larva) || !_mobs.IsAlive(larva) || _containers.IsEntityInContainer(larva)
            || !TryComp<RotLarvaComponent>(larva, out var child) || child.Satiety >= child.MaxSatiety
            || Transform(nester).GridUid != Transform(larva).GridUid)
            return false;
        var ownCore = CompOrNull<RotColonyMemberComponent>(nester)?.Core;
        var childCore = CompOrNull<RotColonyMemberComponent>(larva)?.Core;
        return ownCore == childCore;
    }

    private bool HasProvision(Entity<RotNesterComponent> ent, EntityUid larva)
    {
        _food.Clear();
        _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(larva), ent.Comp.ExistingFoodRange, _food);
        foreach (var (uid, blob) in _food)
        {
            if (blob.Remaining > 0 && !TerminatingOrDeleted(uid)
                && _interaction.InRangeUnobstructed(larva, uid, ent.Comp.ExistingFoodRange))
                return true;
        }
        return false;
    }

    private static RotScavengeDiet GetDiet(RotNesterComponent nester) =>
        new(nester.BloodReagents, nester.BloodPerBite, nester.CorpseMeals);

    private bool HasPortion(RotNesterComponent nester) => nester.Stored >= nester.BitesPerPortion;

    /// <summary>Eats corpses and spilled blood until one portion is stored; the only source of larva food.</summary>
    private bool TryForage(Entity<RotNesterComponent> ent, Entity<RotDefenderComponent> brain)
    {
        if (HasPortion(ent.Comp))
        {
            CancelForage(ent);
            return false;
        }
        if (ent.Comp.Bite != null)
            return true;
        var diet = GetDiet(ent.Comp);
        if (ent.Comp.FoodTarget is { } old && !_nests.CanScavenge(old, diet))
            ent.Comp.FoodTarget = null;
        if (ent.Comp.FoodTarget == null)
        {
            if (_timing.CurTime < ent.Comp.NextFoodSearch)
                return false;
            ent.Comp.NextFoodSearch = _timing.CurTime + ent.Comp.FoodSearchInterval;
            if (!_nests.TryFindScavengeFood(ent, ent.Comp.SearchRange, diet, out var found))
                return false;
            ent.Comp.FoodTarget = found;
        }
        var food = ent.Comp.FoodTarget.Value;
        if (!_interaction.InRangeUnobstructed(ent.Owner, food, ent.Comp.FoodRange))
        {
            if (TryComp<NPCSteeringComponent>(ent, out var steering) && steering.Status == SteeringStatus.NoPath)
            {
                CancelForage(ent);
                return false;
            }
            _navigation.MoveDefender(brain, new EntityCoordinates(food, Vector2.Zero), 0.8f);
            return true;
        }
        _steering.Unregister(ent);
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        var args = new DoAfterArgs(EntityManager, ent, ent.Comp.BiteInterval, new RotNesterBiteEvent(), ent, target: food)
        {
            NeedHand = false,
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = ent.Comp.FoodRange,
            MultiplyDelay = false,
            CancelDuplicate = false,
        };
        if (_doAfter.TryStartDoAfter(args, out ent.Comp.Bite))
            return true;
        CancelForage(ent);
        return false;
    }

    private void OnBite(Entity<RotNesterComponent> ent, ref RotNesterBiteEvent args)
    {
        if (args.Handled || ent.Comp.Bite != args.DoAfter.Id)
            return;
        ent.Comp.Bite = null;
        args.Handled = true;
        if (args.Cancelled || args.Target is not { } food || !_mobs.IsAlive(ent) || _containers.IsEntityInContainer(ent)
            || HasPortion(ent.Comp) || !_interaction.InRangeUnobstructed(ent.Owner, food, ent.Comp.FoodRange)
            || !_nests.TryScavengeBite(food, GetDiet(ent.Comp)))
        {
            ent.Comp.FoodTarget = null;
            return;
        }
        ent.Comp.Stored++;
        // Keep eating without waiting for the next think interval.
        ent.Comp.NextThink = TimeSpan.Zero;
    }

    private void CancelForage(Entity<RotNesterComponent> ent)
    {
        var id = ent.Comp.Bite;
        ent.Comp.Bite = null;
        _doAfter.Cancel(id);
        ent.Comp.FoodTarget = null;
    }

    private void CancelWork(Entity<RotNesterComponent> ent)
    {
        CancelProvision(ent);
        CancelForage(ent);
    }

    private bool TryProvision(Entity<RotNesterComponent> ent, Entity<RotDefenderComponent> brain)
    {
        if (!HasPortion(ent.Comp))
        {
            CancelProvision(ent);
            return false;
        }
        if (ent.Comp.Larva is { } old && (!IsHungryAlly(ent, old) || _timing.CurTime >= ent.Comp.LarvaDeadline
            || HasProvision(ent, old) || TryComp<NPCSteeringComponent>(ent, out var failed) && failed.Status == SteeringStatus.NoPath))
            CancelProvision(ent);
        if (ent.Comp.Vomit != null)
            return true;
        if (ent.Comp.Larva == null)
        {
            var closest = float.MaxValue;
            _larvae.Clear();
            _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(ent), ent.Comp.SearchRange, _larvae);
            foreach (var (uid, _) in _larvae)
            {
                if (!IsHungryAlly(ent, uid) || HasProvision(ent, uid)
                    || TryComp<RotLarvaProvisionComponent>(uid, out var claim) && claim.Running && claim.Nester != ent.Owner
                        && !TerminatingOrDeleted(claim.Nester) && _mobs.IsAlive(claim.Nester) && claim.Expires > _timing.CurTime)
                    continue;
                var distance = Vector2.DistanceSquared(_transform.GetWorldPosition(ent), _transform.GetWorldPosition(uid));
                if (distance >= closest || !_interaction.InRangeUnobstructed(ent.Owner, uid, ent.Comp.SearchRange))
                    continue;
                closest = distance;
                ent.Comp.Larva = uid;
            }
            if (ent.Comp.Larva is { } selected)
            {
                ent.Comp.LarvaDeadline = _timing.CurTime + ent.Comp.ProvisionTimeout;
                var claim = EnsureComp<RotLarvaProvisionComponent>(selected);
                claim.Nester = ent;
                claim.Expires = ent.Comp.LarvaDeadline;
            }
        }
        if (ent.Comp.Larva is not { } larva)
            return false;
        if (!_interaction.InRangeUnobstructed(ent.Owner, larva, ent.Comp.ProvisionRange))
        {
            _navigation.MoveDefender(brain, new EntityCoordinates(larva, Vector2.Zero), 0.8f);
            return true;
        }
        _steering.Unregister(ent);
        RemCompDeferred<NPCMeleeCombatComponent>(ent);
        var args = new DoAfterArgs(EntityManager, ent, ent.Comp.VomitDuration, new RotNesterVomitEvent(), ent, target: larva)
        {
            NeedHand = false,
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = ent.Comp.ProvisionRange,
            MultiplyDelay = false,
            CancelDuplicate = false,
        };
        if (!_doAfter.TryStartDoAfter(args, out ent.Comp.Vomit))
        {
            CancelProvision(ent);
            return false;
        }
        _appearance.SetData(ent, CreatureActivityVisuals.State, ent.Comp.VomitState);
        return true;
    }

    private void OnVomited(Entity<RotNesterComponent> ent, ref RotNesterVomitEvent args)
    {
        if (args.Handled || ent.Comp.Vomit != args.DoAfter.Id)
            return;
        ent.Comp.Vomit = null;
        _appearance.SetData(ent, CreatureActivityVisuals.State, string.Empty);
        if (args.Cancelled || args.Target is not { } larva || !IsHungryAlly(ent, larva) || !_mobs.IsAlive(ent)
            || _containers.IsEntityInContainer(ent) || !HasPortion(ent.Comp)
            || !_interaction.InRangeUnobstructed(ent.Owner, larva, ent.Comp.ProvisionRange) || HasProvision(ent, larva))
        {
            CancelProvision(ent);
            return;
        }
        var uid = Spawn(ent.Comp.Food, Transform(larva).Coordinates);
        var food = EnsureComp<RotNutritionBlobComponent>(uid);
        food.Initial = food.Remaining = Comp<RotLarvaComponent>(larva).MaxSatiety * ent.Comp.LarvaePerPortion;
        _appearance.SetData(uid, RotOrganVisuals.Nutrition, false);
        _colony.Inherit(ent, uid);
        ent.Comp.Stored -= ent.Comp.BitesPerPortion;
        args.Handled = true;
        CancelProvision(ent);
    }

    private void CancelProvision(Entity<RotNesterComponent> ent)
    {
        var id = ent.Comp.Vomit;
        ent.Comp.Vomit = null;
        _doAfter.Cancel(id);
        if (ent.Comp.Larva is { } larva && TryComp<RotLarvaProvisionComponent>(larva, out var claim) && claim.Nester == ent.Owner)
            RemCompDeferred<RotLarvaProvisionComponent>(larva);
        ent.Comp.Larva = null;
        _appearance.SetData(ent, CreatureActivityVisuals.State, string.Empty);
    }
}
