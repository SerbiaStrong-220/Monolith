using Content.Server.Body.Components;
using Content.Server.Medical;
using Content.Shared._Exodus.Nutrition;
using Content.Shared._Exodus.Virology.Behaviors;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Virology.Behaviors;

public sealed partial class BloodVomitSystem : EntitySystem
{
    [Dependency] private VomitSystem _vomit = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BloodVomitComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BloodVomitComponent, VomitEvent>(OnVomit);
    }

    private void OnStartup(Entity<BloodVomitComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.NextVomit = _timing.CurTime + ent.Comp.Interval;
    }

    private void OnVomit(Entity<BloodVomitComponent> ent, ref VomitEvent args)
    {
        if (ent.Comp.BloodAmount <= FixedPoint2.Zero
            || !TryComp<BloodstreamComponent>(ent, out var bloodstream)
            || !_solutions.ResolveSolution(ent.Owner, bloodstream.BloodSolutionName, ref bloodstream.BloodSolution))
            return;

        // Splitting the real bloodstream preserves species-specific blood, donor DNA and virus metadata.
        var blood = _solutions.SplitSolution(bloodstream.BloodSolution!.Value, ent.Comp.BloodAmount);
        args.Solution.AddSolution(blood, _prototypes);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<BloodVomitComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Interval <= TimeSpan.Zero || now < comp.NextVomit)
                continue;

            // Missed attacks must not accumulate into a burst after a server hitch.
            var elapsedIntervals = (now - comp.NextVomit).Ticks / comp.Interval.Ticks + 1;
            comp.NextVomit += comp.Interval * elapsedIntervals;
            if (!_mobState.IsDead(uid))
                _vomit.Vomit(uid);
        }
    }
}
