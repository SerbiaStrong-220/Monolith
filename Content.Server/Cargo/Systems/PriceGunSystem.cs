using Content.Server._Exodus.Economy; // Exodus market appraisal
using Content.Server.Popups;
using Content.Shared.Cargo.Components;
using Content.Shared.IdentityManagement;
using Content.Shared.Timing;
using Content.Shared.Cargo.Systems;
using Robust.Shared.Audio.Systems;

namespace Content.Server.Cargo.Systems;

public sealed partial class PriceGunSystem : SharedPriceGunSystem
{
    [Dependency] private UseDelaySystem _useDelay = default!;
    [Dependency] private MarketAppraisalSystem _marketAppraisal = default!; // Exodus market appraisal
    [Dependency] private PopupSystem _popupSystem = default!;
    [Dependency] private CargoSystem _bountySystem = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    protected override bool GetPriceOrBounty(Entity<PriceGunComponent> entity, EntityUid target, EntityUid user)
    {
        if (!TryComp(entity.Owner, out UseDelayComponent? useDelay) || _useDelay.IsDelayed((entity.Owner, useDelay)))
            return false;
        _useDelay.TryResetDelay((entity.Owner, useDelay)); // Exodus: throttle the calculation and its result event together.
        // Check if we're scanning a bounty crate
        if (_bountySystem.IsBountyComplete(target, out _))
        {
            _popupSystem.PopupEntity(Loc.GetString("price-gun-bounty-complete"), user, user);
        }
        // Exodus-begin: quote the current market without committing a sale; share the result with PDA history.
        else if (_marketAppraisal.TryGetEntitySellPrice(target, out var price, CompOrNull<TransformComponent>(target)?.GridUid))
        {
            price = DynamicMarketSystem.RoundSellPayout(price);
            _popupSystem.PopupEntity(Loc.GetString("price-gun-pricing-result",
                    ("object", Identity.Entity(target, EntityManager)),
                    ("price", $"{price:F2}")),
                user,
                user);

            var ev = new PriceGunAppraisedEvent(target, price);
            RaiseLocalEvent(entity.Owner, ref ev);
        }
        else
        {
            _popupSystem.PopupEntity(Loc.GetString("market-appraisal-unavailable"), user, user);
        }
        // Exodus-end

        _audio.PlayPvs(entity.Comp.AppraisalSound, entity.Owner);
        // Exodus: the use delay starts before appraisal and result dispatch.
        return true;
    }
}
