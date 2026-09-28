using Content.Server._Exodus.War;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.War;
using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Communications;

public sealed partial class CommunicationsConsoleSystem
{
    [Dependency] private FactionWarSystem _factionWar = default!;
    [Dependency] private IPrototypeManager _warPrototype = default!;

    private WarDeclarationConsoleState? BuildWarDeclarationState(EntityUid uid)
    {
        if (!TryComp<WarDeclarationConsoleComponent>(uid, out var console) ||
            !_factionWar.TryGetState(out var warState) ||
            !_warPrototype.TryIndex(console.Faction, out var sourcePrototype))
        {
            return null;
        }

        var configuredTargets = new List<ProtoId<TerritoryFactionPrototype>>();
        _factionWar.GetConfiguredTargets(warState, (uid, console), configuredTargets);

        var targets = new List<WarDeclarationTargetState>(configuredTargets.Count);
        foreach (var target in configuredTargets)
        {
            if (!_warPrototype.TryIndex(target, out var targetPrototype))
                continue;

            var direction = WarDeclarationDirection.None;
            var peaceDirection = PeaceOfferDirection.None;
            var peaceOfferId = 0;
            var peaceAvailableAt = TimeSpan.Zero;
            if (_factionWar.TryGetDeclaration(warState, console.Faction, target, out var declaration))
            {
                direction = declaration.DeclaringFaction == console.Faction
                    ? WarDeclarationDirection.Outgoing
                    : WarDeclarationDirection.Incoming;

                peaceAvailableAt = _factionWar.GetPeaceOfferAvailableAt(declaration);
                if (declaration.PeaceOffer is { } offer)
                {
                    peaceDirection = offer.OfferingFaction == console.Faction
                        ? PeaceOfferDirection.Outgoing
                        : PeaceOfferDirection.Incoming;
                    peaceOfferId = offer.Id;
                }
            }

            var allianceDirection = AllianceOfferDirection.None;
            var allianceOfferId = 0;
            var allianceAvailableAt = TimeSpan.Zero;
            if (_factionWar.TryGetAllianceEntry(warState, console.Faction, target, out var allianceEntry))
            {
                allianceAvailableAt = _factionWar.GetAllianceOfferAvailableAt(allianceEntry);
                if (allianceEntry.Offer is { } allianceOffer)
                {
                    allianceDirection = allianceOffer.OfferingFaction == console.Faction
                        ? AllianceOfferDirection.Outgoing
                        : AllianceOfferDirection.Incoming;
                    allianceOfferId = allianceOffer.Id;
                }
            }

            _factionWar.TryGetWarLock(warState, console.Faction, target, out var lockUntil, out var lockReason);

            targets.Add(new WarDeclarationTargetState(
                target,
                targetPrototype.DisplayName ?? targetPrototype.RadarLabel,
                direction,
                _factionWar.GetDeclarationAvailableAt(warState, console.Faction, target),
                peaceDirection,
                peaceOfferId,
                peaceAvailableAt,
                _factionWar.GetRelation(warState, console.Faction, target),
                allianceDirection,
                allianceOfferId,
                allianceAvailableAt,
                lockUntil,
                lockReason));
        }

        var relations = new List<FactionRelationState>(warState.Comp.Factions.Count);
        foreach (var faction in warState.Comp.Factions)
        {
            if (!_warPrototype.TryIndex(faction, out var factionPrototype))
                continue;

            relations.Add(new FactionRelationState(
                faction,
                factionPrototype.DisplayName ?? factionPrototype.RadarLabel,
                $"war-faction-short-{faction.Id}",
                factionPrototype.Color));
        }

        var pairs = new List<FactionPairRelationState>();
        for (var i = 0; i < warState.Comp.Factions.Count; i++)
        {
            for (var j = i + 1; j < warState.Comp.Factions.Count; j++)
            {
                var first = warState.Comp.Factions[i];
                var second = warState.Comp.Factions[j];
                pairs.Add(new FactionPairRelationState(
                    first,
                    second,
                    _factionWar.GetRelation(warState, first, second)));
            }
        }

        return new WarDeclarationConsoleState(
            console.Faction,
            sourcePrototype.DisplayName ?? sourcePrototype.RadarLabel,
            _factionWar.IsRoundRunning,
            _factionWar.CodeAllowsWar(),
            targets,
            relations,
            pairs,
            BuildCorporationStates(),
            warState.Comp.AllianceBreakCooldown);
    }

    private List<CorporationTerritoryState> BuildCorporationStates()
    {
        var owners = new Dictionary<ProtoId<CompanyPrototype>, ProtoId<TerritoryFactionPrototype>>();
        // Allegiance also includes claims on paused grids, as in CompanyTerritoryBannerSystem.
        var territories = AllEntityQuery<GridTerritoryComponent>();
        while (territories.MoveNext(out _, out var territory))
        {
            if (territory.CorporateController is not { } company ||
                territory.ControllingFaction is not { } faction ||
                territory.ActiveCorporateBanner is not { } banner ||
                !TryComp<CompanyTerritoryBannerComponent>(banner, out var bannerComp) ||
                bannerComp.Company != company ||
                bannerComp.TerritoryFaction != faction)
            {
                continue;
            }

            owners[company] = faction;
        }

        var corporations = new List<CorporationTerritoryState>();
        foreach (var company in _warPrototype.EnumeratePrototypes<CompanyPrototype>())
        {
            if (!company.DiplomacyVisible)
                continue;

            ProtoId<TerritoryFactionPrototype>? owner = null;
            if (owners.TryGetValue(company.ID, out var faction))
                owner = faction;

            corporations.Add(new CorporationTerritoryState(company.ID, owner));
        }

        return corporations;
    }
}
