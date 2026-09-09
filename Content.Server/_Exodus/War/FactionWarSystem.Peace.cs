using Content.Server._Mono.AlertLevel;
using Content.Shared._Exodus.Territory;
using Content.Shared.Database;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.War;

public sealed partial class FactionWarSystem
{
    public TimeSpan GetDeclarationAvailableAt(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second)
    {
        var availableAt = GetDeclarationAvailableAt(state);
        if (TryGetWarCooldown(state, first, second, out var cooldown))
        {
            var pairAvailableAt = _ticker.RoundStartTimeSpan + cooldown.AvailableAtRoundTime;
            if (pairAvailableAt > availableAt)
                availableAt = pairAvailableAt;
        }

        return availableAt;
    }

    public TimeSpan GetPeaceOfferAvailableAt(FactionWarDeclaration declaration)
    {
        return _ticker.RoundStartTimeSpan + declaration.NextPeaceOfferAtRoundTime;
    }

    public PeaceOfferResult TryOfferPeace(
        ProtoId<TerritoryFactionPrototype> offeringFaction,
        ProtoId<TerritoryFactionPrototype> target,
        EntityUid? actor = null,
        EntityUid? source = null)
    {
        var validation = ValidatePeaceAction(offeringFaction, target, out var state, out var declaration);
        if (validation != PeaceOfferResult.Success)
            return validation;

        if (declaration.PeaceOffer != null)
            return PeaceOfferResult.AlreadyPending;

        if (_ticker.RoundDuration() < declaration.NextPeaceOfferAtRoundTime)
            return PeaceOfferResult.Cooldown;

        declaration.PeaceOffer = new FactionPeaceOffer
        {
            Id = ++state.Comp.NextPeaceOfferId,
            OfferingFaction = offeringFaction,
        };

        LogPeaceAction("offered peace to", offeringFaction, target, actor, source);
        var ev = new PeaceOfferChangedEvent();
        RaiseLocalEvent(ref ev);
        return PeaceOfferResult.Success;
    }

    public PeaceOfferResult TryWithdrawPeace(
        ProtoId<TerritoryFactionPrototype> offeringFaction,
        ProtoId<TerritoryFactionPrototype> target,
        int offerId,
        EntityUid? actor = null,
        EntityUid? source = null)
    {
        var validation = ValidatePeaceAction(offeringFaction, target, out var state, out var declaration);
        if (validation != PeaceOfferResult.Success)
            return validation;

        if (declaration.PeaceOffer is not { } offer || offer.Id != offerId)
            return PeaceOfferResult.OfferUnavailable;

        if (offer.OfferingFaction != offeringFaction)
            return PeaceOfferResult.NotOfferSender;

        declaration.PeaceOffer = null;
        declaration.NextPeaceOfferAtRoundTime = _ticker.RoundDuration() + state.Comp.PeaceOfferCooldown;

        LogPeaceAction("withdrew the peace offer to", offeringFaction, target, actor, source);
        var ev = new PeaceOfferChangedEvent();
        RaiseLocalEvent(ref ev);
        return PeaceOfferResult.Success;
    }

    public PeaceOfferResult TryAcceptPeace(
        ProtoId<TerritoryFactionPrototype> acceptingFaction,
        ProtoId<TerritoryFactionPrototype> target,
        int offerId,
        EntityUid? actor = null,
        EntityUid? source = null)
    {
        var validation = ValidatePeaceAction(acceptingFaction, target, out var state, out var declaration);
        if (validation != PeaceOfferResult.Success)
            return validation;

        if (declaration.PeaceOffer is not { } offer || offer.Id != offerId)
            return PeaceOfferResult.OfferUnavailable;

        if (offer.OfferingFaction != target)
            return PeaceOfferResult.NotOfferRecipient;

        var result = TryEndWar(acceptingFaction, target, actor, source, announce: false);
        if (result != WarDeclarationResult.Success)
            return PeaceOfferResult.NotAtWar;

        LogPeaceAction("accepted the peace offer from", acceptingFaction, target, actor, source);
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("war-peace-agreed-announcement",
                ("first", GetFactionName(target)),
                ("second", GetFactionName(acceptingFaction)),
                ("minutes", state.Comp.PostWarCooldown.TotalMinutes)),
            sender: Loc.GetString("war-declaration-announcement-sender"),
            announcementSound: PeaceSound,
            colorOverride: Color.CornflowerBlue);
        return PeaceOfferResult.Success;
    }

    private PeaceOfferResult ValidatePeaceAction(
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second,
        out Entity<WarLevelComponent> state,
        out FactionWarDeclaration declaration)
    {
        declaration = null!;
        if (!TryGetState(out state))
            return PeaceOfferResult.StateUnavailable;

        if (!IsRoundRunning)
            return PeaceOfferResult.RoundNotRunning;

        if (ValidatePair(state, first, second) != WarDeclarationResult.Success)
            return PeaceOfferResult.InvalidFaction;

        return TryGetDeclaration(state, first, second, out declaration)
            ? PeaceOfferResult.Success
            : PeaceOfferResult.NotAtWar;
    }

    private bool TryGetWarCooldown(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second,
        out FactionWarCooldown cooldown)
    {
        foreach (var candidate in state.Comp.WarCooldowns)
        {
            if (candidate.FirstFaction == first && candidate.SecondFaction == second ||
                candidate.FirstFaction == second && candidate.SecondFaction == first)
            {
                cooldown = candidate;
                return true;
            }
        }

        cooldown = null!;
        return false;
    }

    private void StartWarCooldown(Entity<WarLevelComponent> state, FactionWarDeclaration declaration)
    {
        if (!TryGetWarCooldown(state, declaration.DeclaringFaction, declaration.TargetFaction, out var cooldown))
        {
            cooldown = new FactionWarCooldown
            {
                FirstFaction = declaration.DeclaringFaction,
                SecondFaction = declaration.TargetFaction,
            };
            state.Comp.WarCooldowns.Add(cooldown);
        }

        cooldown.AvailableAtRoundTime = GetRoundTimeForLog() + state.Comp.PostWarCooldown;
    }

    private void LogPeaceAction(
        string action,
        ProtoId<TerritoryFactionPrototype> faction,
        ProtoId<TerritoryFactionPrototype> target,
        EntityUid? actor,
        EntityUid? source)
    {
        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(actor):player} as {faction.Id} {action} {target.Id} using {ToPrettyString(source):entity} at round time {GetRoundTimeForLog()}");
    }
}
