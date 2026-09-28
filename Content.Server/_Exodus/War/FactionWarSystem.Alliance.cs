using Content.Server._Mono.AlertLevel;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.War;
using Content.Shared.Database;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.War;

public sealed partial class FactionWarSystem
{
    public TimeSpan GetAllianceOfferAvailableAt(FactionAllianceOfferEntry entry)
    {
        return _ticker.RoundStartTimeSpan + entry.NextOfferAtRoundTime;
    }

    public bool IsAllied(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second)
    {
        return TryGetAlliance(state, first, second, out _);
    }

    public FactionRelationKind GetRelation(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second)
    {
        if (TryGetDeclaration(state, first, second, out _))
            return FactionRelationKind.War;

        if (IsAllied(state, first, second))
            return FactionRelationKind.Alliance;

        return FactionRelationKind.Neutral;
    }

    public bool TryGetWarLock(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second,
        out TimeSpan availableAt,
        out WarLockReason reason)
    {
        availableAt = TimeSpan.Zero;
        reason = WarLockReason.None;
        if (!TryGetWarCooldown(state, first, second, out var cooldown))
            return false;

        availableAt = _ticker.RoundStartTimeSpan + cooldown.AvailableAtRoundTime;
        reason = cooldown.Reason;
        return true;
    }

    public AllianceOfferResult OfferAlliance(
        ProtoId<TerritoryFactionPrototype> offeringFaction,
        ProtoId<TerritoryFactionPrototype> target,
        EntityUid? actor = null,
        EntityUid? source = null)
    {
        if (!TryGetState(out var state))
            return AllianceOfferResult.StateUnavailable;

        if (!IsRoundRunning)
            return AllianceOfferResult.RoundNotRunning;

        if (ValidatePair(state, offeringFaction, target) != WarDeclarationResult.Success)
            return AllianceOfferResult.InvalidFaction;

        if (TryGetDeclaration(state, offeringFaction, target, out _))
            return AllianceOfferResult.AtWar;

        if (IsAllied(state, offeringFaction, target))
            return AllianceOfferResult.AlreadyAllied;

        var entry = GetOrCreateAllianceEntry(state, offeringFaction, target);
        if (entry.Offer != null)
            return AllianceOfferResult.AlreadyPending;

        if (_ticker.RoundDuration() < entry.NextOfferAtRoundTime)
            return AllianceOfferResult.Cooldown;

        entry.Offer = new FactionAllianceOffer
        {
            Id = ++state.Comp.NextAllianceOfferId,
            OfferingFaction = offeringFaction,
        };

        LogAllianceAction("offered an alliance to", offeringFaction, target, actor, source);
        var ev = new AllianceOfferChangedEvent();
        RaiseLocalEvent(ref ev);
        return AllianceOfferResult.Success;
    }

    public AllianceOfferResult WithdrawAlliance(
        ProtoId<TerritoryFactionPrototype> offeringFaction,
        ProtoId<TerritoryFactionPrototype> target,
        int offerId,
        EntityUid? actor = null,
        EntityUid? source = null)
    {
        if (!TryPrepareAllianceOffer(offeringFaction, target, out var state, out var entry, out var failure))
            return failure;

        if (entry.Offer is not { } offer || offer.Id != offerId)
            return AllianceOfferResult.OfferUnavailable;

        if (offer.OfferingFaction != offeringFaction)
            return AllianceOfferResult.NotOfferSender;

        entry.Offer = null;
        entry.NextOfferAtRoundTime = _ticker.RoundDuration() + state.Comp.AllianceOfferCooldown;

        LogAllianceAction("withdrew the alliance offer to", offeringFaction, target, actor, source);
        var ev = new AllianceOfferChangedEvent();
        RaiseLocalEvent(ref ev);
        return AllianceOfferResult.Success;
    }

    public AllianceOfferResult AcceptAlliance(
        ProtoId<TerritoryFactionPrototype> acceptingFaction,
        ProtoId<TerritoryFactionPrototype> target,
        int offerId,
        EntityUid? actor = null,
        EntityUid? source = null)
    {
        if (!TryPrepareAllianceOffer(acceptingFaction, target, out var state, out var entry, out var failure))
            return failure;

        if (entry.Offer is not { } offer || offer.Id != offerId)
            return AllianceOfferResult.OfferUnavailable;

        if (offer.OfferingFaction != target)
            return AllianceOfferResult.NotOfferRecipient;

        if (TryGetDeclaration(state, acceptingFaction, target, out _))
            return AllianceOfferResult.AtWar;

        if (IsAllied(state, acceptingFaction, target))
            return AllianceOfferResult.AlreadyAllied;

        state.Comp.Alliances.Add(new FactionAlliance
        {
            FirstFaction = entry.FirstFaction,
            SecondFaction = entry.SecondFaction,
        });
        entry.Offer = null;

        LogAllianceAction("accepted the alliance offer from", acceptingFaction, target, actor, source);
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("war-alliance-formed-announcement",
                ("first", GetFactionName(target)),
                ("second", GetFactionName(acceptingFaction))),
            sender: Loc.GetString("war-declaration-announcement-sender"),
            announcementSound: PeaceSound,
            colorOverride: Color.LimeGreen);
        var ev = new AllianceOfferChangedEvent();
        RaiseLocalEvent(ref ev);
        return AllianceOfferResult.Success;
    }

    public AllianceOfferResult BreakAlliance(
        ProtoId<TerritoryFactionPrototype> faction,
        ProtoId<TerritoryFactionPrototype> target,
        EntityUid? actor = null,
        EntityUid? source = null)
    {
        if (!TryGetState(out var state))
            return AllianceOfferResult.StateUnavailable;

        if (!IsRoundRunning)
            return AllianceOfferResult.RoundNotRunning;

        if (ValidatePair(state, faction, target) != WarDeclarationResult.Success)
            return AllianceOfferResult.InvalidFaction;

        if (!RemoveAlliance(state, faction, target))
            return AllianceOfferResult.NotAllied;

        StartPairCooldown(state, faction, target, state.Comp.AllianceBreakCooldown, WarLockReason.AllianceBreak);
        CancelAllianceOffer(state, faction, target);

        LogAllianceAction("broke the alliance with", faction, target, actor, source);
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("war-alliance-broken-announcement",
                ("first", GetFactionName(faction)),
                ("second", GetFactionName(target)),
                ("minutes", (int)state.Comp.AllianceBreakCooldown.TotalMinutes)),
            sender: Loc.GetString("war-declaration-announcement-sender"),
            announcementSound: PeaceSound,
            colorOverride: Color.Goldenrod);
        var ev = new AllianceOfferChangedEvent();
        RaiseLocalEvent(ref ev);
        return AllianceOfferResult.Success;
    }

    private bool TryGetAlliance(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second,
        out FactionAlliance alliance)
    {
        foreach (var candidate in state.Comp.Alliances)
        {
            if (candidate.FirstFaction == first && candidate.SecondFaction == second ||
                candidate.FirstFaction == second && candidate.SecondFaction == first)
            {
                alliance = candidate;
                return true;
            }
        }

        alliance = null!;
        return false;
    }

    private bool RemoveAlliance(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second)
    {
        if (!TryGetAlliance(state, first, second, out var alliance))
            return false;

        state.Comp.Alliances.Remove(alliance);
        return true;
    }

    public bool TryGetAllianceEntry(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second,
        out FactionAllianceOfferEntry entry)
    {
        foreach (var candidate in state.Comp.AllianceOffers)
        {
            if (candidate.FirstFaction == first && candidate.SecondFaction == second ||
                candidate.FirstFaction == second && candidate.SecondFaction == first)
            {
                entry = candidate;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    private FactionAllianceOfferEntry GetOrCreateAllianceEntry(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second)
    {
        if (TryGetAllianceEntry(state, first, second, out var entry))
            return entry;

        entry = new FactionAllianceOfferEntry
        {
            FirstFaction = first,
            SecondFaction = second,
        };
        state.Comp.AllianceOffers.Add(entry);
        return entry;
    }

    private void CancelAllianceOffer(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second)
    {
        if (TryGetAllianceEntry(state, first, second, out var entry))
            entry.Offer = null;
    }

    private bool TryPrepareAllianceOffer(
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second,
        out Entity<WarLevelComponent> state,
        out FactionAllianceOfferEntry entry,
        out AllianceOfferResult failure)
    {
        entry = null!;
        failure = AllianceOfferResult.Success;
        if (!TryGetState(out state))
        {
            failure = AllianceOfferResult.StateUnavailable;
            return false;
        }

        if (!IsRoundRunning)
        {
            failure = AllianceOfferResult.RoundNotRunning;
            return false;
        }

        if (ValidatePair(state, first, second) != WarDeclarationResult.Success)
        {
            failure = AllianceOfferResult.InvalidFaction;
            return false;
        }

        if (TryGetAllianceEntry(state, first, second, out entry))
            return true;

        failure = AllianceOfferResult.OfferUnavailable;
        return false;
    }

    private void StartPairCooldown(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second,
        TimeSpan duration,
        WarLockReason reason)
    {
        if (!TryGetWarCooldown(state, first, second, out var cooldown))
        {
            cooldown = new FactionWarCooldown
            {
                FirstFaction = first,
                SecondFaction = second,
            };
            state.Comp.WarCooldowns.Add(cooldown);
        }

        cooldown.AvailableAtRoundTime = GetRoundTimeForLog() + duration;
        cooldown.Reason = reason;
    }

    private void LogAllianceAction(
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
