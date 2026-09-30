using System.Numerics;
using Content.Server.Body.Components;
using Content.Server._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Intelligent;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotNestSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private RotOrganDecaySystem _organDecay = default!;
    private readonly HashSet<Entity<PuddleComponent>> _foodPuddles = [];
    private readonly HashSet<Entity<RotNutritionBlobComponent>> _foodBlobs = [];

    private void InitializeFeeding()
    {
        SubscribeLocalEvent<RotLarvaComponent, RotLarvaFeedEvent>(OnFed);
    }

    private static RotScavengeDiet GetDiet(RotLarvaComponent larva) =>
        new(larva.BloodReagents, larva.BloodPerBite, larva.MaxSatiety * larva.MealsPerCorpse);

    public bool TryFindFood(Entity<RotLarvaComponent> ent, out EntityUid target)
    {
        target = default;
        if (ent.Comp.Satiety >= ent.Comp.MaxSatiety || _mobs.IsDead(ent) || _containers.IsEntityInContainer(ent))
            return false;

        var origin = _transform.GetMapCoordinates(ent);
        var closest = float.MaxValue;
        FindScavengeFood(ent, origin, ent.Comp.SearchRange, GetDiet(ent.Comp), ref closest, ref target);
        _foodBlobs.Clear();
        _lookup.GetEntitiesInRange(origin, ent.Comp.SearchRange, _foodBlobs);
        foreach (var (uid, blob) in _foodBlobs)
        {
            if (blob.Remaining > 0 && !TerminatingOrDeleted(uid) && !_containers.IsEntityInContainer(uid))
                ConsiderFood(ent, uid, origin.Position, ent.Comp.SearchRange, ref closest, ref target);
        }
        return target.IsValid();
    }

    /// <summary>Finds the nearest reachable corpse or blood puddle a rot scavenger can bite.</summary>
    public bool TryFindScavengeFood(EntityUid eater, float range, RotScavengeDiet diet, out EntityUid target)
    {
        target = default;
        var closest = float.MaxValue;
        FindScavengeFood(eater, _transform.GetMapCoordinates(eater), range, diet, ref closest, ref target);
        return target.IsValid();
    }

    private void FindScavengeFood(EntityUid eater, MapCoordinates origin, float range, RotScavengeDiet diet,
        ref float closest, ref EntityUid target)
    {
        _bodies.Clear();
        _lookup.GetEntitiesInRange(origin, range, _bodies);
        foreach (var (uid, _) in _bodies)
        {
            if (CanEat(uid))
                ConsiderFood(eater, uid, origin.Position, range, ref closest, ref target);
        }
        _foodPuddles.Clear();
        _lookup.GetEntitiesInRange(origin, range, _foodPuddles);
        foreach (var (uid, _) in _foodPuddles)
        {
            if (HasBloodToBite(uid, diet))
                ConsiderFood(eater, uid, origin.Position, range, ref closest, ref target);
        }
    }

    private void ConsiderFood(EntityUid eater, EntityUid food, Vector2 origin, float range, ref float closest, ref EntityUid target)
    {
        var distance = Vector2.DistanceSquared(origin, _transform.GetMapCoordinates(food).Position);
        if (distance >= closest || !_interaction.InRangeUnobstructed(eater, food, range))
            return;
        closest = distance;
        target = food;
    }

    public bool CanFeedOn(Entity<RotLarvaComponent> ent, EntityUid food)
    {
        if (TryComp<RotNutritionBlobComponent>(food, out var blob))
            return blob.Remaining > 0 && !TerminatingOrDeleted(food) && !_containers.IsEntityInContainer(food);
        return CanScavenge(food, GetDiet(ent.Comp));
    }

    /// <summary>Whether a corpse or a blood puddle still offers a bite under this diet.</summary>
    public bool CanScavenge(EntityUid food, RotScavengeDiet diet)
    {
        if (TerminatingOrDeleted(food) || _containers.IsEntityInContainer(food))
            return false;
        return HasComp<PuddleComponent>(food) ? HasBloodToBite(food, diet) : CanEat(food);
    }

    private bool HasBloodToBite(EntityUid puddle, RotScavengeDiet diet)
    {
        if (diet.BloodPerBite <= FixedPoint2.Zero || TerminatingOrDeleted(puddle) || _containers.IsEntityInContainer(puddle)
            || !TryComp<PuddleComponent>(puddle, out var comp)
            || !_solutions.TryGetSolution(puddle, comp.SolutionName, out _, out var solution))
            return false;
        var amount = FixedPoint2.Zero;
        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (diet.BloodReagents.Contains(reagent.Prototype))
                amount += quantity;
        }
        return amount >= diet.BloodPerBite;
    }

    public bool CanEat(EntityUid uid) => !TerminatingOrDeleted(uid) && _mobs.IsDead(uid)
        && HasComp<BodyComponent>(uid) && !HasComp<RotCreatureComponent>(uid) && !_containers.IsEntityInContainer(uid)
        && (!TryComp<RotCorpseNutritionComponent>(uid, out var food) || food.Remaining > 0)
        && (HasEdibleLimb(uid) || HasBlood(uid));

    private bool HasBlood(EntityUid uid) => TryComp<BloodstreamComponent>(uid, out var blood)
        && _solutions.ResolveSolution(uid, blood.BloodSolutionName, ref blood.BloodSolution, out var solution)
        && solution.Volume > FixedPoint2.Zero;

    private bool HasEdibleLimb(EntityUid uid)
    {
        foreach (var (partUid, part) in _body.GetBodyChildren(uid))
        {
            if (part.PartType is BodyPartType.Arm or BodyPartType.Leg
                && _body.GetParentPartAndSlotOrNull(partUid) != null)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Takes one bite of a corpse or blood puddle. Corpses share one finite pool between every scavenger,
    /// so larvae and nesters never create food from nothing.
    /// </summary>
    public bool TryScavengeBite(EntityUid food, RotScavengeDiet diet)
    {
        if (!CanScavenge(food, diet))
            return false;
        if (TryComp<PuddleComponent>(food, out var puddle))
        {
            if (!_solutions.TryGetSolution(food, puddle.SolutionName, out var solutionEntity, out var solution))
                return false;
            var remaining = diet.BloodPerBite;
            // Remove only blood, preserving water, toxins and the reagent's infection data.
            for (var i = solution.Contents.Count - 1; i >= 0 && remaining > FixedPoint2.Zero; i--)
            {
                var (reagent, quantity) = solution.Contents[i];
                if (!diet.BloodReagents.Contains(reagent.Prototype))
                    continue;
                var taken = FixedPoint2.Min(quantity, remaining);
                solution.RemoveReagent(reagent, taken);
                remaining -= taken;
            }
            _solutions.UpdateChemicals(solutionEntity.Value);
            return true;
        }
        if (!TryComp<RotCorpseNutritionComponent>(food, out var nutrition))
        {
            nutrition = AddComp<RotCorpseNutritionComponent>(food);
            nutrition.Remaining = diet.CorpseMeals;
        }
        if (TryComp<BloodstreamComponent>(food, out var blood)
            && _solutions.ResolveSolution(food, blood.BloodSolutionName, ref blood.BloodSolution, out var bloodSolution))
            _solutions.SplitSolution(blood.BloodSolution.Value,
                FixedPoint2.Max(FixedPoint2.New(0.001), bloodSolution.Volume / nutrition.Remaining));
        nutrition.Remaining--;
        return true;
    }

    public bool TryFeed(Entity<RotLarvaComponent> ent, EntityUid food)
    {
        if (ent.Comp.FeedDoAfter != null)
            return true;
        if (!CanFeedOn(ent, food) || _mobs.IsDead(ent) || ent.Comp.Satiety >= ent.Comp.MaxSatiety
            || _containers.IsEntityInContainer(ent) || !_interaction.InRangeUnobstructed(ent.Owner, food, 1.2f))
            return false;
        var args = new DoAfterArgs(EntityManager, ent, ent.Comp.BiteInterval, new RotLarvaFeedEvent(), ent, target: food)
        {
            NeedHand = false,
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = 1.2f,
            MultiplyDelay = false,
            CancelDuplicate = false,
        };
        return _doAfter.TryStartDoAfter(args, out ent.Comp.FeedDoAfter);
    }

    public void CancelFeeding(Entity<RotLarvaComponent> ent)
    {
        if (ent.Comp.FeedDoAfter is { } id)
            _doAfter.Cancel(id);
        ent.Comp.FeedDoAfter = null;
    }

    private void OnFed(Entity<RotLarvaComponent> ent, ref RotLarvaFeedEvent args)
    {
        if (args.Handled || ent.Comp.FeedDoAfter != args.DoAfter.Id)
            return;
        ent.Comp.FeedDoAfter = null;
        if (args.Cancelled || args.Target is not { } food || !CanFeedOn(ent, food)
            || _mobs.IsDead(ent) || ent.Comp.Satiety >= ent.Comp.MaxSatiety || _containers.IsEntityInContainer(ent)
            || !_interaction.InRangeUnobstructed(ent.Owner, food, 1.2f))
            return;

        if (TryComp<RotNutritionBlobComponent>(food, out var blob))
        {
            blob.Remaining--;
            _appearance.SetData(food, RotOrganVisuals.Nutrition, blob.Remaining <= blob.Initial / 2);
            if (blob.Remaining == 0)
            {
                _organDecay.Play(food);
                QueueDel(food);
            }
        }
        else if (!TryScavengeBite(food, GetDiet(ent.Comp)))
            return;

        args.Handled = true;
        ent.Comp.Satiety++;
        if (ent.Comp.Satiety < ent.Comp.MaxSatiety)
            return;
        if (HasComp<BodyComponent>(food))
            ConsumeLimb(food);
        ent.Comp.PupateBy = _timing.CurTime + TimeSpan.FromSeconds(_random.NextDouble(
            ent.Comp.ShelterSearchMin.TotalSeconds, ent.Comp.ShelterSearchMax.TotalSeconds));
        _steering.Unregister(ent);
        UpdateLarvaVisual(ent);
    }

    private void UpdateLarvaVisual(Entity<RotLarvaComponent> ent)
    {
        var state = _mobs.IsDead(ent) ? RotLarvaState.Dead
            : ent.Comp.HatchAt != null ? RotLarvaState.Pupa
            : ent.Comp.Satiety >= ent.Comp.MaxSatiety ? RotLarvaState.Sated : RotLarvaState.Hungry;
        _appearance.SetData(ent, RotLarvaVisuals.State, state);
    }

    private void ConsumeLimb(EntityUid corpse)
    {
        foreach (var (uid, part) in _body.GetBodyChildren(corpse))
        {
            if (part.PartType is not (BodyPartType.Arm or BodyPartType.Leg)
                || _body.GetParentPartAndSlotOrNull(uid) is not { } parent)
                continue;
            if (_body.DetachPart(parent.Parent, parent.Slot, uid))
                QueueDel(uid);
            break;
        }
    }
}
