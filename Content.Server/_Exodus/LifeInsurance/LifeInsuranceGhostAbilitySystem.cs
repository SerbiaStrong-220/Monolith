using Content.Server.Popups;
using Content.Shared._Exodus.LifeInsurance;
using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared.Actions;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.LifeInsurance;

public sealed class LifeInsuranceGhostAbilitySystem : EntitySystem
{
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private LifeInsuranceConsoleSystem _console = default!;
    [Dependency] private LifeInsuranceClonerSystem _cloner = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    [ValidatePrototypeId<EntityPrototype>]
    private const string ActionProto = "ActionLifeInsuranceRevive";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GhostComponent, PlayerAttachedEvent>(OnGhostPlayerAttached);
        SubscribeLocalEvent<LifeInsuranceUserComponent, LifeInsuranceCloneActionEvent>(OnActivate);
    }

    private void OnGhostPlayerAttached(Entity<GhostComponent> ent, ref PlayerAttachedEvent args)
    {
        var user = args.Player.UserId;

        var charges = _console.GetInsuranceCount(user);
        if (charges <= 0)
            return;

        var comp = EnsureComp<LifeInsuranceUserComponent>(ent);

        if (!Exists(comp.ActionId))
            comp.ActionId = _actions.AddAction(ent, ActionProto);

        _actions.SetCharges(comp.ActionId, charges);
    }

    private void OnActivate(Entity<LifeInsuranceUserComponent> ent, ref LifeInsuranceCloneActionEvent args)
    {
        if (args.Handled)
            return;

        // Only a ghost can revive.
        if (!HasComp<GhostComponent>(ent))
            return;

        if (!_mind.TryGetMind(ent, out var mindId, out var mind) || mind.UserId is not { } user)
            return;

        // Is body alive? tracked on OriginalOwnedEntity.
        if (TryGetEntity(mind.OriginalOwnedEntity, out var body) && _mobState.IsAlive(body.Value))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-original-alive"), ent, ent);
            return;
        }

        if (!_console.TryFindInsurance(user, out var console, out _, out var record))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-no-active-policy"), ent, ent);
            return;
        }

        if (!TryComp<LifeInsuranceConsoleComponent>(console, out var consoleComp) ||
            consoleComp.Cloner is not { } cloner ||
            !_cloner.IsAvailable(cloner))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-cloner-unavailable"), ent, ent);
            return;
        }

        if (!_cloner.TryStartRevival(cloner, record.Profile, mindId, user, record.Company))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-cloner-unavailable"), ent, ent);
            return;
        }

        record.Insurances--;
        _console.UpdateUi(console);

        args.Handled = true;
    }
}
