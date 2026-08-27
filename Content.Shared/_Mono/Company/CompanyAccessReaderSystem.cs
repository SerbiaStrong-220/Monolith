using Content.Shared.Access.Components; // Exodus-company-card-access
using Content.Shared.Access.Systems; // Exodus-company-card-access
using Content.Shared.Hands.EntitySystems; // Exodus-company-card-access
using Content.Shared.Interaction; // Exodus-company-card-access
using Content.Shared.Inventory; // Exodus-company-card-access
using Content.Shared.Popups;
using Content.Shared.Storage.Components; // Exodus-company-card-access
using Content.Shared.Storage.EntitySystems; // Exodus-company-card-access
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects; // Exodus-company-card-access

namespace Content.Shared._Mono.Company;

/// <summary>
/// This system handles checking if a user belongs to the required company
/// before granting access to an entity.
/// </summary>
public sealed partial class CompanyAccessReaderSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    // Exodus-begin company-card-access
    [Dependency] private SharedHandsSystem _hands = default!; // Exodus-company-card-access
    [Dependency] private SharedIdCardSystem _idCard = default!; // Exodus-company-card-access
    [Dependency] private InventorySystem _inventory = default!; // Exodus-company-card-access
    [Dependency] private SharedUserInterfaceSystem _ui = default!; // Exodus-company-card-access

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CompanyAccessReaderComponent, ActivatableUIOpenAttemptEvent>(OnUIOpenAttempt);
        SubscribeLocalEvent<CompanyAccessReaderComponent, ActivateInWorldEvent>(OnActivate, before: [typeof(SharedStorageSystem)]); // Exodus-company-card-access
        SubscribeLocalEvent<CompanyAccessReaderComponent, BoundUIOpenedEvent>(OnBoundUIOpened); // Exodus-company-card-access
        SubscribeLocalEvent<CompanyAccessReaderComponent, StorageOpenAttemptEvent>(OnStorageOpenAttempt); // Exodus-company-card-access
        SubscribeLocalEvent<CompanyAccessReaderComponent, DumpableDoAfterEvent>(OnDump,
            before: [typeof(DumpableSystem)]); // Exodus-company-card-access
    }

    private void OnActivate(Entity<CompanyAccessReaderComponent> entity, ref ActivateInWorldEvent args) // Exodus-company-card-access
    {
        if (args.Handled || !entity.Comp.RequireCompanyCard || IsAllowed(entity.Comp, args.User))
            return;

        args.Handled = true;
        ShowDeniedPopup(entity, args.User);
    }

    private void OnBoundUIOpened(Entity<CompanyAccessReaderComponent> entity, ref BoundUIOpenedEvent args) // Exodus-company-card-access
    {
        if (!entity.Comp.RequireCompanyCard || IsAllowed(entity.Comp, args.Actor))
            return;

        _ui.CloseUi(entity.Owner, args.UiKey, args.Actor);
        ShowDeniedPopup(entity, args.Actor);
    }

    private void OnStorageOpenAttempt(Entity<CompanyAccessReaderComponent> entity, ref StorageOpenAttemptEvent args) // Exodus-company-card-access
    {
        if (args.Cancelled || !entity.Comp.RequireCompanyCard || IsAllowed(entity.Comp, args.User))
            return;

        args.Cancelled = true;
        ShowDeniedPopup(entity, args.User);
    }

    private void OnDump(Entity<CompanyAccessReaderComponent> entity, ref DumpableDoAfterEvent args) // Exodus-company-card-access
    {
        if (args.Handled || args.Cancelled || !entity.Comp.RequireCompanyCard || IsAllowed(entity.Comp, args.User))
            return;

        args.Handled = true;
        ShowDeniedPopup(entity, args.User);
    }

    private void OnUIOpenAttempt(Entity<CompanyAccessReaderComponent> entity, ref ActivatableUIOpenAttemptEvent args)
    {
        if (args.Cancelled || IsAllowed(entity.Comp, args.User))
            return;

        args.Cancel();
        ShowDeniedPopup(entity, args.User);
    }

    private bool IsAllowed(CompanyAccessReaderComponent component, EntityUid user)
    {
        if (component.RequireCompanyCard)
            return HasAllowedCompanyCard(component, user);

        if (!TryComp<CompanyComponent>(user, out var userCompany))
            return component.Inverted;

        return component.RequiredCompanies.Contains(userCompany.CompanyName) != component.Inverted;
    }

    private bool HasAllowedCompanyCard(CompanyAccessReaderComponent component, EntityUid user)
    {
        if (_idCard.TryGetIdCard(user, out var idCard) && IsCardAllowed(component, idCard.Comp))
            return true;

        foreach (var item in _hands.EnumerateHeld(user))
        {
            if (_idCard.TryGetIdCard(item, out idCard) && IsCardAllowed(component, idCard.Comp))
                return true;
        }

        if (_inventory.TryGetContainerSlotEnumerator(user, out var enumerator))
        {
            while (enumerator.NextItem(out var item))
            {
                if (_idCard.TryGetIdCard(item, out idCard) && IsCardAllowed(component, idCard.Comp))
                    return true;
            }
        }

        return false;
    }

    private static bool IsCardAllowed(CompanyAccessReaderComponent component, IdCardComponent idCard)
    {
        if (idCard.CompanyName.Id == "None")
            return false;

        return component.RequiredCompanies.Count == 0
            ? !component.Inverted
            : component.RequiredCompanies.Contains(idCard.CompanyName) != component.Inverted;
    }

    private void ShowDeniedPopup(Entity<CompanyAccessReaderComponent> entity, EntityUid user)
    {
        if (entity.Comp.PopupMessage != null)
            _popup.PopupClient(Loc.GetString(entity.Comp.PopupMessage), entity, user);
    }
    // Exodus-end company-card-access
}
