using Content.Server.GameTicking;
using Content.Server.Station.Systems;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared._Exodus.Shuttles;
using Content.Shared._Exodus.Shuttles.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Shuttles.Systems;

public sealed partial class ShuttleEventBeaconSystem : EntitySystem
{
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private AlertLevelInterceptionRule _alertLevelInterceptionRule = default!;
    [Dependency] private IffAffiliationSystem _iffAffiliation = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShuttleEventBeaconComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<ShuttleEventBeaconComponent, ActivateInWorldEvent>(OnActivateInWorld);
    }

    private void OnUseInHand(Entity<ShuttleEventBeaconComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        args.ApplyDelay = false;
        TryActivate(ent, args.User);
    }

    private void OnActivateInWorld(Entity<ShuttleEventBeaconComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex)
            return;

        args.Handled = true;
        TryActivate(ent, args.User);
    }

    private void TryActivate(Entity<ShuttleEventBeaconComponent> ent, EntityUid user)
    {
        if (TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent.Owner))
            return;

        if (_gameTicker.IsGameRuleAdded(ent.Comp.Rule))
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.FailurePopup), user, user);
            return;
        }

        if (!_gameTicker.TryForceAddGameRule(
                ent.Comp.Rule,
                out var addedRule,
                rule => TryInitializeRule(rule, user)))
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.FailurePopup), user, user);
            return;
        }

        var ruleUid = addedRule.Value;

        if (!_gameTicker.StartGameRule(ruleUid))
        {
            QueueDel(ruleUid);
            _popup.PopupEntity(Loc.GetString(ent.Comp.FailurePopup), user, user);
            return;
        }

        ApplyShuttleAffiliation(ent, ruleUid);
        _popup.PopupEntity(Loc.GetString(ent.Comp.SuccessPopup), user, user);

        if (ent.Comp.ConsumeOnSuccess)
            QueueDel(ent.Owner);
    }

    private void ApplyShuttleAffiliation(Entity<ShuttleEventBeaconComponent> ent, EntityUid rule)
    {
        // LoadMapRule loads and records grids synchronously when the rule is added.
        // Entity-only events have no RuleGrids and deliberately receive no grid affiliation.
        if (ent.Comp.ShuttleFaction is not { } faction ||
            TerminatingOrDeleted(rule) || !TryComp<RuleGridsComponent>(rule, out var grids))
        {
            return;
        }

        foreach (var grid in grids.MapGrids)
        {
            if (TerminatingOrDeleted(grid) || EntityManager.IsQueuedForDeletion(grid) ||
                !HasComp<MapGridComponent>(grid))
                continue;

            var affiliation = EnsureComp<IffAffiliationComponent>(grid);
            _iffAffiliation.SetFaction((grid, affiliation), faction);
        }
    }

    private bool TryInitializeRule(EntityUid rule, EntityUid user)
    {
        if (!HasComp<AlertLevelInterceptionRuleComponent>(rule))
            return true;

        return TryResolveAlertTargetStation(user, out var targetStation) &&
               _alertLevelInterceptionRule.TrySetTargetStation(rule, targetStation);
    }

    private bool TryResolveAlertTargetStation(EntityUid user, out EntityUid station)
    {
        if (_station.GetOwningStation(user) is { } owningStation)
        {
            station = owningStation;
            return true;
        }

        foreach (var stationUid in _station.GetStationsSet())
        {
            station = stationUid;
            return true;
        }

        station = default;
        return false;
    }
}
