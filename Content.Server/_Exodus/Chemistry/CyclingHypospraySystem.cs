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

namespace Content.Server._Exodus.Chemistry;

public sealed class CyclingHypospraySystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly ReagentAutoRechargeSystem _reagentAutoRecharge = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CyclingHyposprayComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CyclingHyposprayComponent, UseInHandEvent>(OnUseInHand, before: [typeof(HypospraySystem)]);
        SubscribeLocalEvent<CyclingHyposprayComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<CyclingHyposprayComponent, AfterInteractEvent>(OnAfterInteract, before: [typeof(HypospraySystem)]);
    }

    private void OnMapInit(Entity<CyclingHyposprayComponent> ent, ref MapInitEvent args)
    {
        NormalizeSolution(ent);

        if (!TryGetCurrentReagent(ent.Comp, out var reagent))
            return;

        if (TryComp<ReagentAutoRechargeComponent>(ent, out var recharge))
            _reagentAutoRecharge.SetReagent((ent, recharge), reagent);
    }

    private void OnUseInHand(Entity<CyclingHyposprayComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        CycleReagent(ent, args.User);
        args.Handled = true;
        args.ApplyDelay = false;
    }

    private void OnActivate(Entity<CyclingHyposprayComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        CycleReagent(ent, args.User);
        args.Handled = true;
    }

    private void OnAfterInteract(Entity<CyclingHyposprayComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target == null)
            return;

        if (HasComp<MobStateComponent>(args.Target.Value))
            return;

        _popup.PopupEntity(Loc.GetString("hypospray-cant-inject", ("target", Identity.Name(args.Target.Value, EntityManager))), args.Target.Value, args.User);
        args.Handled = true;
    }

    private void CycleReagent(Entity<CyclingHyposprayComponent> ent, EntityUid user)
    {
        if (ent.Comp.Reagents.Count == 0)
            return;

        ent.Comp.CurrentReagent = (ent.Comp.CurrentReagent + 1) % ent.Comp.Reagents.Count;
        NormalizeSolution(ent);

        if (!TryGetCurrentReagent(ent.Comp, out var reagent))
            return;

        _popup.PopupEntity(Loc.GetString("borg-paramedic-hypo-selected", ("reagent", GetReagentName(reagent))), ent, user);

        if (TryComp<ReagentAutoRechargeComponent>(ent, out var recharge))
            _reagentAutoRecharge.SetReagent((ent, recharge), reagent);
    }

    private void NormalizeSolution(Entity<CyclingHyposprayComponent> ent)
    {
        if (!_solutions.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var solutionEnt, out var solution))
            return;

        NormalizeSolution(ent, solutionEnt.Value, solution);
    }

    private void NormalizeSolution(Entity<CyclingHyposprayComponent> ent, Entity<SolutionComponent> solutionEnt, Solution solution)
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

    private bool TryGetCurrentReagent(CyclingHyposprayComponent component, out ProtoId<ReagentPrototype> reagent)
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
