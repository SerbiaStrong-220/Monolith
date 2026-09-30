using Content.Shared.Access.Components; // Exodus-company-card-access
using Content.Shared.Access.Systems; // Exodus-company-card-access
using Content.Shared._Exodus.Company; // Exodus concern migration
using Content.Shared._Exodus.Access; // Exodus - universal company access.
using Content.Shared.Hands.EntitySystems; // Exodus-company-card-access
using Content.Shared.Interaction; // Exodus-company-card-access
using Content.Shared.Inventory; // Exodus-company-card-access
using Content.Shared.Popups;
using Content.Shared.Storage.Components; // Exodus-company-card-access
using Content.Shared.Storage.EntitySystems; // Exodus-company-card-access
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects; // Exodus-company-card-access
using Robust.Shared.Prototypes; // Exodus concern migration

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
    [Dependency] private IPrototypeManager _prototypes = default!; // Exodus concern migration

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
        // Exodus - universal access works directly on a user or through their admin ID card.
        if (HasComp<UniversalAccessComponent>(user) || HasAllowedCompanyCard(component, user))
            return true;

        if (component.RequireCompanyCard)
            return false; // Exodus - company cards were checked above.

        if (!TryComp<CompanyComponent>(user, out var userCompany))
            return component.Inverted;

        return MatchesCompany(component, userCompany.CompanyName) != component.Inverted; // Exodus concern migration
    }

    private bool HasAllowedCompanyCard(CompanyAccessReaderComponent component, EntityUid user)
    {
        if (_idCard.TryGetIdCard(user, out var idCard) && IsCardAllowed(component, idCard)) // Exodus - retain card UID.
            return true;

        foreach (var item in _hands.EnumerateHeld(user))
        {
            if (_idCard.TryGetIdCard(item, out idCard) && IsCardAllowed(component, idCard)) // Exodus - retain card UID.
                return true;
        }

        if (_inventory.TryGetContainerSlotEnumerator(user, out var enumerator))
        {
            while (enumerator.NextItem(out var item))
            {
                if (_idCard.TryGetIdCard(item, out idCard) && IsCardAllowed(component, idCard)) // Exodus - retain card UID.
                    return true;
            }
        }

        return false;
    }

    private bool IsCardAllowed(CompanyAccessReaderComponent component, Entity<IdCardComponent> idCard) // Exodus - universal company access.
    {
        // Exodus-begin - universal cards pass both company membership and company card checks.
        if (HasComp<UniversalAccessComponent>(idCard.Owner))
            return true;

        if (!component.RequireCompanyCard || idCard.Comp.CompanyName.Id == "None")
            return false;
        // Exodus-end

        return component.RequiredCompanies.Count == 0
            ? !component.Inverted
            : MatchesCompany(component, idCard.Comp.CompanyName) != component.Inverted; // Exodus - retain card UID.
    }

    // Exodus-begin concern migration
    private bool MatchesCompany(CompanyAccessReaderComponent component, ProtoId<CompanyPrototype> company)
    {
        var currentCompany = CompanyConsolidation.Normalize(company.Id, _prototypes);
        foreach (var required in component.RequiredCompanies)
        {
            if (CompanyConsolidation.Normalize(required.Id, _prototypes) == currentCompany)
                return true;
        }

        return false;
    }
    // Exodus-end

    private void ShowDeniedPopup(Entity<CompanyAccessReaderComponent> entity, EntityUid user)
    {
        if (entity.Comp.PopupMessage != null)
            _popup.PopupClient(Loc.GetString(entity.Comp.PopupMessage), entity, user);
    }
    // Exodus-end company-card-access
}
