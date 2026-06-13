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

/// <summary>
/// Grants insured ghosts the "Activate Life Insurance" action and handles its activation,
/// starting a clone on the registered cloner and consuming one insurance charge.
/// </summary>
public sealed class LifeInsuranceGhostAbilitySystem : EntitySystem
{
    [Dependency] private readonly SharedMindSystem _mind = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly LifeInsuranceConsoleSystem _console = default!;
    [Dependency] private readonly LifeInsuranceClonerSystem _cloner = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    [ValidatePrototypeId<EntityPrototype>]
    private const string ActionProto = "ActionLifeInsuranceRevive";

    public override void Initialize()
    {
        base.Initialize();

        // GhostComponent's MindAddedMessage is already taken by GhostSystem (one directed sub per comp+event),
        // so hook PlayerAttachedEvent instead — it also fires on reconnect and gives the session directly.
        SubscribeLocalEvent<GhostComponent, PlayerAttachedEvent>(OnGhostPlayerAttached);
        SubscribeLocalEvent<LifeInsuranceUserComponent, LifeInsuranceCloneActionEvent>(OnActivate);
    }

    private void OnGhostPlayerAttached(EntityUid uid, GhostComponent ghost, PlayerAttachedEvent args)
    {
        var user = args.Player.UserId;

        var charges = _console.GetInsuranceCount(user);
        if (charges <= 0)
            return;

        var comp = EnsureComp<LifeInsuranceUserComponent>(uid);

        if (!Exists(comp.ActionId))
            comp.ActionId = _actions.AddAction(uid, ActionProto);

        _actions.SetCharges(comp.ActionId, charges);
    }

    private void OnActivate(EntityUid uid, LifeInsuranceUserComponent comp, LifeInsuranceCloneActionEvent args)
    {
        if (args.Handled)
            return;

        // Safety: only a ghost may revive. Prevents a living holder of this action from killing
        // themselves by force-transferring their mind into a fresh clone.
        if (!HasComp<GhostComponent>(uid))
            return;

        if (!_mind.TryGetMind(uid, out var mindId, out var mind) || mind.UserId is not { } user)
            return;

        // Anti-dupe: don't revive if the player's original body is still alive (e.g. ghosting while
        // alive). Their mind would be yanked into a clone, leaving a live abandoned body behind.
        if (mind.OwnedEntity is { } original && Exists(original) && _mobState.IsAlive(original))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-original-alive"), uid, uid);
            return;
        }

        if (!_console.TryFindInsurance(user, out var console, out _, out var record))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-no-active-policy"), uid, uid);
            return;
        }

        if (!TryComp<LifeInsuranceConsoleComponent>(console, out var consoleComp) ||
            consoleComp.Cloner is not { } cloner ||
            !_cloner.IsAvailable(cloner))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-cloner-unavailable"), uid, uid);
            return;
        }

        if (!_cloner.TryStartRevival(cloner, record.Profile, mindId, user))
        {
            _popup.PopupEntity(Loc.GetString("life-insurance-cloner-unavailable"), uid, uid);
            return;
        }

        // The action system auto-consumes one charge because we set Handled below; mirror that on the registry.
        record.Insurances--;
        _console.UpdateUi(console);

        args.Handled = true;
    }
}
