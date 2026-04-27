using Content.Server.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Silicons.Borgs;

public sealed class BorgParamedicHypoSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BorgParamedicHypoComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<BorgParamedicHypoComponent, UseInHandEvent>(OnUseInHand, before: [typeof(HypospraySystem)]);
        SubscribeLocalEvent<BorgParamedicHypoComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<BorgParamedicHypoComponent, AfterInteractEvent>(OnAfterInteract, before: [typeof(HypospraySystem)]);
    }

    private void OnMapInit(Entity<BorgParamedicHypoComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextRecharge = _timing.CurTime + ent.Comp.RechargeInterval;
        NormalizeSolution(ent);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BorgParamedicHypoComponent>();
        while (query.MoveNext(out var uid, out var hypo))
        {
            if (hypo.RechargeInterval <= TimeSpan.Zero || hypo.RechargeAmount <= FixedPoint2.Zero)
                continue;

            if (hypo.NextRecharge == TimeSpan.Zero)
            {
                hypo.NextRecharge = _timing.CurTime + hypo.RechargeInterval;
                continue;
            }

            if (_timing.CurTime < hypo.NextRecharge)
                continue;

            Recharge((uid, hypo));
            hypo.NextRecharge += hypo.RechargeInterval;

            if (hypo.NextRecharge < _timing.CurTime)
                hypo.NextRecharge = _timing.CurTime + hypo.RechargeInterval;
        }
    }

    private void OnUseInHand(Entity<BorgParamedicHypoComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        CycleReagent(ent, args.User);
        args.Handled = true;
        args.ApplyDelay = false;
    }

    private void OnActivate(Entity<BorgParamedicHypoComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        CycleReagent(ent, args.User);
        args.Handled = true;
    }

    private void OnAfterInteract(Entity<BorgParamedicHypoComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        if (HasComp<MobStateComponent>(args.Target.Value))
            return;

        _popup.PopupEntity(Loc.GetString("hypospray-cant-inject", ("target", Identity.Entity(args.Target.Value, EntityManager))), args.Target.Value, args.User);
        args.Handled = true;
    }

    private void CycleReagent(Entity<BorgParamedicHypoComponent> ent, EntityUid user)
    {
        if (ent.Comp.Reagents.Count == 0)
            return;

        ent.Comp.CurrentReagent = (ent.Comp.CurrentReagent + 1) % ent.Comp.Reagents.Count;
        NormalizeSolution(ent);

        if (!TryGetCurrentReagent(ent.Comp, out var reagent))
            return;

        _popup.PopupEntity(Loc.GetString("borg-paramedic-hypo-selected", ("reagent", GetReagentName(reagent))), ent, user);
    }

    private void Recharge(Entity<BorgParamedicHypoComponent> ent)
    {
        if (!TryGetCurrentReagent(ent.Comp, out var reagent))
            return;

        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;

        NormalizeSolution(ent, solutionEnt.Value, solution);

        var amount = FixedPoint2.Min(ent.Comp.RechargeAmount, solution.AvailableVolume);
        if (amount <= FixedPoint2.Zero)
            return;

        _solutions.TryAddReagent(solutionEnt.Value, reagent, amount, out _);
    }

    private void NormalizeSolution(Entity<BorgParamedicHypoComponent> ent)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;

        NormalizeSolution(ent, solutionEnt.Value, solution);
    }

    private void NormalizeSolution(Entity<BorgParamedicHypoComponent> ent, Entity<SolutionComponent> solutionEnt, Solution solution)
    {
        if (!TryGetCurrentReagent(ent.Comp, out var reagent))
            return;

        var volume = FixedPoint2.Min(solution.Volume, solution.MaxVolume);
        if (volume <= FixedPoint2.Zero)
            return;

        if (solution.Contents.Count == 1 && solution.Contents[0].Reagent.Prototype == reagent)
            return;

        _solutions.RemoveAllSolution(solutionEnt);
        _solutions.TryAddReagent(solutionEnt, reagent, volume, out _);
    }

    private bool TryGetCurrentReagent(BorgParamedicHypoComponent component, out ProtoId<ReagentPrototype> reagent)
    {
        reagent = default;
        if (component.Reagents.Count == 0)
            return false;

        if (component.CurrentReagent < 0 || component.CurrentReagent >= component.Reagents.Count)
            component.CurrentReagent = 0;

        reagent = component.Reagents[component.CurrentReagent];
        return true;
    }

    private string GetReagentName(ProtoId<ReagentPrototype> reagent)
    {
        return _prototype.TryIndex(reagent, out var prototype)
            ? prototype.LocalizedName
            : reagent;
    }
}
