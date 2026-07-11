using Content.Shared.Store;
using JetBrains.Annotations;
using System.Linq;
using Content.Shared.Store.Components;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client.Store.Ui;

[UsedImplicitly]
public sealed class StoreBoundUserInterface : BoundUserInterface
{
    private IPrototypeManager _prototypeManager = default!;

    [ViewVariables]
    private StoreMenu? _menu;

    [ViewVariables]
    private string _search = string.Empty;

    [ViewVariables]
    private HashSet<ListingDataWithCostModifiers> _listings = new();

    // Exodus
    [ViewVariables]
    private StoreUiMode _mode = StoreUiMode.Default;

    // Exodus
    [ViewVariables]
    private bool _hasPriceModifier;

    // Exodus
    [ViewVariables]
    private float _priceMultiplier;

    // Exodus
    [ViewVariables]
    private float _summoningPriceMultiplier = 1f;

    // Exodus
    [ViewVariables]
    private bool _summoningBusy;

    public StoreBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<StoreMenu>();
        if (EntMan.TryGetComponent<StoreComponent>(Owner, out var store))
            _menu.Title = Loc.GetString(store.Name);

        _menu.OnListingButtonPressed += (_, listing) =>
        {
            SendMessage(new StoreBuyListingMessage(listing.ID));
        };

        _menu.OnCategoryButtonPressed += (_, category) =>
        {
            _menu.CurrentCategory = category;
            _menu?.UpdateListing();
        };

        _menu.OnWithdrawAttempt += (_, type, amount) =>
        {
            SendMessage(new StoreRequestWithdrawMessage(type, amount));
        };

        _menu.SearchTextUpdated += (_, search) =>
        {
            _search = search.Trim().ToLowerInvariant();
            UpdateListingsWithSearchFilter();
        };

        _menu.OnRefundAttempt += (_) =>
        {
            SendMessage(new StoreRequestRefundMessage());
        };
    }
    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        switch (state)
        {
            case StoreUpdateState msg:
                // Exodus
                var listingsChanged = !ListingsEqual(_listings, msg.Listings);
                var modeChanged = _mode != msg.Mode ||
                                  _hasPriceModifier != msg.HasPriceModifier ||
                                  Math.Abs(_priceMultiplier - msg.PriceMultiplier) > 0.001f ||
                                  Math.Abs(_summoningPriceMultiplier - msg.SummoningPriceMultiplier) > 0.001f;
                var summoningBusy = msg.ActiveSummoning != null;
                var busyChanged = _summoningBusy != summoningBusy;

                _listings = msg.Listings;
                _mode = msg.Mode;
                _hasPriceModifier = msg.HasPriceModifier;
                _priceMultiplier = msg.PriceMultiplier;
                _summoningPriceMultiplier = msg.SummoningPriceMultiplier;
                _summoningBusy = summoningBusy;

                _menu?.SetMode(msg.Mode, msg.HasPriceModifier, msg.PriceMultiplier, msg.SummoningPriceMultiplier);
                _menu?.UpdateBalance(msg.Balance);
                _menu?.SetSummoning(msg.ActiveSummoning);

                if (listingsChanged || modeChanged || busyChanged)
                    UpdateListingsWithSearchFilter();
                _menu?.SetFooterVisibility(msg.ShowFooter);
                _menu?.UpdateRefund(msg.AllowRefund);
                break;
        }
    }

    private void UpdateListingsWithSearchFilter()
    {
        if (_menu == null)
            return;

        var filteredListings = new HashSet<ListingDataWithCostModifiers>(_listings);
        if (!string.IsNullOrEmpty(_search))
        {
            filteredListings.RemoveWhere(listingData => !ListingLocalisationHelpers.GetLocalisedNameOrEntityName(listingData, _prototypeManager).Trim().ToLowerInvariant().Contains(_search) &&
                                                        !ListingLocalisationHelpers.GetLocalisedDescriptionOrEntityDescription(listingData, _prototypeManager).Trim().ToLowerInvariant().Contains(_search));
        }
        _menu.SetAllListings(_listings.ToList());
        _menu.PopulateStoreCategoryButtons(filteredListings);
        _menu.UpdateListing(filteredListings.ToList());
    }

    // Exodus
    private static bool ListingsEqual(HashSet<ListingDataWithCostModifiers> left, HashSet<ListingDataWithCostModifiers> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var listing in left)
        {
            var found = false;
            foreach (var other in right)
            {
                if (other.ID != listing.ID)
                    continue;

                if (!listing.Equals(other))
                    return false;

                found = true;
                break;
            }

            if (!found)
                return false;
        }

        return true;
    }
}
