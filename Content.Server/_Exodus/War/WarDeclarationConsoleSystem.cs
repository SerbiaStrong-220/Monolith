using Content.Server._Exodus.Biocode;
using Content.Server._Exodus.Communications;
using Content.Server._Mono.AlertLevel;
using Content.Server.Popups;
using Content.Shared._Exodus.Biocode;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.War;
using Content.Shared.Access.Systems;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.War;

public sealed class WarDeclarationConsoleSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private BiocodeSystem _biocode = default!;
    [Dependency] private CommunicationsConsoleSystem _communications = default!;
    [Dependency] private FactionWarSystem _factionWar = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private PopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WarDeclarationConsoleComponent, CommunicationsConsoleDeclareWarMessage>(OnDeclareWar);
        SubscribeLocalEvent<WarDeclarationConsoleComponent, CommunicationsConsoleOfferPeaceMessage>(OnOfferPeace);
        SubscribeLocalEvent<WarDeclarationConsoleComponent, CommunicationsConsoleAcceptPeaceMessage>(OnAcceptPeace);
        SubscribeLocalEvent<WarDeclarationConsoleComponent, CommunicationsConsoleWithdrawPeaceMessage>(OnWithdrawPeace);
        SubscribeLocalEvent<WarLevelChangedEvent>(OnWarLevelChanged);
        SubscribeLocalEvent<PeaceOfferChangedEvent>(OnPeaceOfferChanged);
    }

    private void OnDeclareWar(
        Entity<WarDeclarationConsoleComponent> ent,
        ref CommunicationsConsoleDeclareWarMessage args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        if (!TryValidateConsoleAction(ent, actor, args.TargetFaction, out var warState))
            return;

        var result = _factionWar.TryDeclareWar(
            ent.Comp.Faction,
            args.TargetFaction,
            actor,
            ent.Owner);

        var popup = result switch
        {
            WarDeclarationResult.Success => null,
            WarDeclarationResult.TooEarly => "war-declaration-too-early",
            WarDeclarationResult.PostWarCooldown => "war-declaration-post-war-cooldown",
            WarDeclarationResult.AlreadyAtWar => "war-declaration-already-active",
            WarDeclarationResult.RoundNotRunning => "war-declaration-round-not-running",
            _ => "war-declaration-failed",
        };

        if (popup == null)
            return;

        var availableAt = _factionWar.GetDeclarationAvailableAt(warState, ent.Comp.Faction, args.TargetFaction);
        _popup.PopupEntity(
            Loc.GetString(popup, ("time", FormatRemaining(availableAt))),
            ent,
            actor,
            PopupType.Medium);
    }

    private void OnOfferPeace(Entity<WarDeclarationConsoleComponent> ent, ref CommunicationsConsoleOfferPeaceMessage args)
    {
        if (args.Actor is not { Valid: true } actor ||
            !TryValidateConsoleAction(ent, actor, args.TargetFaction, out var state))
        {
            return;
        }

        var result = _factionWar.TryOfferPeace(ent.Comp.Faction, args.TargetFaction, actor, ent.Owner);
        var availableAt = _factionWar.TryGetDeclaration(state, ent.Comp.Faction, args.TargetFaction, out var declaration)
            ? _factionWar.GetPeaceOfferAvailableAt(declaration)
            : TimeSpan.Zero;
        ShowPeaceResult(ent, actor, result, "war-peace-offer-sent", availableAt);
    }

    private void OnAcceptPeace(Entity<WarDeclarationConsoleComponent> ent, ref CommunicationsConsoleAcceptPeaceMessage args)
    {
        if (args.Actor is not { Valid: true } actor ||
            !TryValidateConsoleAction(ent, actor, args.TargetFaction, out _))
        {
            return;
        }

        var result = _factionWar.TryAcceptPeace(ent.Comp.Faction, args.TargetFaction, args.OfferId, actor, ent.Owner);
        ShowPeaceResult(ent, actor, result, "war-peace-offer-accepted");
    }

    private void OnWithdrawPeace(Entity<WarDeclarationConsoleComponent> ent, ref CommunicationsConsoleWithdrawPeaceMessage args)
    {
        if (args.Actor is not { Valid: true } actor ||
            !TryValidateConsoleAction(ent, actor, args.TargetFaction, out _))
        {
            return;
        }

        var result = _factionWar.TryWithdrawPeace(ent.Comp.Faction, args.TargetFaction, args.OfferId, actor, ent.Owner);
        ShowPeaceResult(ent, actor, result, "war-peace-offer-withdrawn");
    }

    private bool TryValidateConsoleAction(
        Entity<WarDeclarationConsoleComponent> ent,
        EntityUid actor,
        ProtoId<TerritoryFactionPrototype> target,
        out Entity<WarLevelComponent> state)
    {
        if (!_factionWar.TryGetState(out state) || !_factionWar.IsConfiguredTarget(state, ent, target))
        {
            _popup.PopupEntity(Loc.GetString("war-declaration-invalid-target"), ent, actor, PopupType.Medium);
            return false;
        }

        if (!IsAuthorized(ent, actor))
        {
            _popup.PopupEntity(Loc.GetString("war-declaration-no-access"), ent, actor, PopupType.Medium);
            return false;
        }

        return true;
    }

    private void ShowPeaceResult(
        Entity<WarDeclarationConsoleComponent> ent,
        EntityUid actor,
        PeaceOfferResult result,
        string successMessage,
        TimeSpan availableAt = default)
    {
        var message = result switch
        {
            PeaceOfferResult.Success => successMessage,
            PeaceOfferResult.RoundNotRunning => "war-peace-round-not-running",
            PeaceOfferResult.NotAtWar => "war-peace-not-at-war",
            PeaceOfferResult.AlreadyPending => "war-peace-already-pending",
            PeaceOfferResult.Cooldown => "war-peace-offer-cooldown",
            PeaceOfferResult.OfferUnavailable => "war-peace-offer-unavailable",
            PeaceOfferResult.NotOfferSender => "war-peace-not-offer-sender",
            PeaceOfferResult.NotOfferRecipient => "war-peace-not-offer-recipient",
            _ => "war-peace-failed",
        };
        _popup.PopupEntity(Loc.GetString(message, ("time", FormatRemaining(availableAt))), ent, actor, PopupType.Medium);
    }

    private string FormatRemaining(TimeSpan availableAt)
    {
        var seconds = Math.Max(0, Math.Ceiling((availableAt - _timing.CurTime).TotalSeconds));
        var remaining = TimeSpan.FromSeconds(seconds);
        return $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    private bool IsAuthorized(Entity<WarDeclarationConsoleComponent> ent, EntityUid actor)
    {
        var hasRequirement = false;

        if (ent.Comp.RequireBiocode)
        {
            hasRequirement = true;
            if (!TryComp<BiocodeComponent>(ent.Owner, out var biocode) ||
                !_biocode.IsAllowed((ent.Owner, biocode), actor))
            {
                return false;
            }
        }

        if (ent.Comp.RequiredAccess.Count != 0)
        {
            hasRequirement = true;
            var allowed = false;
            var availableAccess = _access.FindAccessTags(actor);
            foreach (var required in ent.Comp.RequiredAccess)
            {
                if (!availableAccess.Contains(required))
                    continue;

                allowed = true;
                break;
            }

            if (!allowed)
                return false;
        }

        return hasRequirement;
    }

    private void OnWarLevelChanged(WarLevelChangedEvent args)
    {
        _communications.UpdateCommsConsoleInterface();
    }

    private void OnPeaceOfferChanged(ref PeaceOfferChangedEvent args)
    {
        _communications.UpdateCommsConsoleInterface();
    }
}
