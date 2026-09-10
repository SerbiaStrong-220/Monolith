using Content.Server._Exodus.War;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.War;
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

            targets.Add(new WarDeclarationTargetState(
                target,
                targetPrototype.DisplayName ?? targetPrototype.RadarLabel,
                direction,
                _factionWar.GetDeclarationAvailableAt(warState, console.Faction, target),
                peaceDirection,
                peaceOfferId,
                peaceAvailableAt));
        }

        return new WarDeclarationConsoleState(
            console.Faction,
            sourcePrototype.DisplayName ?? sourcePrototype.RadarLabel,
            _factionWar.IsRoundRunning,
            _factionWar.GetDeclarationAvailableAt(warState),
            targets);
    }
}
