using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Chemistry;

public sealed class ReagentAutoRechargeSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ReagentAutoRechargeComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ReagentAutoRechargeComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextRecharge = _timing.CurTime + ent.Comp.RechargeInterval;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ReagentAutoRechargeComponent>();
        while (query.MoveNext(out var uid, out var recharge))
        {
            if (recharge.RechargeInterval <= TimeSpan.Zero || recharge.RechargeAmount <= FixedPoint2.Zero)
                continue;

            if (recharge.NextRecharge == TimeSpan.Zero)
            {
                recharge.NextRecharge = _timing.CurTime + recharge.RechargeInterval;
                continue;
            }

            if (_timing.CurTime < recharge.NextRecharge)
                continue;

            Recharge((uid, recharge));
            recharge.NextRecharge += recharge.RechargeInterval;

            if (recharge.NextRecharge < _timing.CurTime)
                recharge.NextRecharge = _timing.CurTime + recharge.RechargeInterval;
        }
    }

    public void SetReagent(Entity<ReagentAutoRechargeComponent> ent, ProtoId<ReagentPrototype> reagent)
    {
        ent.Comp.Reagent = reagent;
    }

    private void Recharge(Entity<ReagentAutoRechargeComponent> ent)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;

        var amount = FixedPoint2.Min(ent.Comp.RechargeAmount, solution.AvailableVolume);
        if (amount <= FixedPoint2.Zero)
            return;

        _solutions.TryAddReagent(solutionEnt.Value, ent.Comp.Reagent, amount, out _);
    }
}
