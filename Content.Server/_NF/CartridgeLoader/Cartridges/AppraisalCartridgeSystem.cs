using Content.Server._Exodus.Economy; // Exodus market appraisal
using Content.Server.Cargo.Systems;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Content.Shared.Timing;
using Content.Shared.Cargo.Components;
using Content.Shared.IdentityManagement; // Exodus identity-aware appraisal history

namespace Content.Server.CartridgeLoader.Cartridges;

public sealed partial class AppraisalCartridgeSystem : EntitySystem
{
    [Dependency] private CargoSystem _bountySystem = default!;
    [Dependency] private CartridgeLoaderSystem? _cartridgeLoaderSystem = default!;
    [Dependency] private IRobustRandom _random = default!;
    // Exodus: prices come from the completed scanner event, without a second appraisal.
    [Dependency] private SharedAudioSystem _audioSystem = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AppraisalCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<CartridgeLoaderComponent, PriceGunAppraisedEvent>(OnPriceGunAppraised); // Exodus reuse scan result
        SubscribeLocalEvent<AppraisalCartridgeComponent, CartridgeActivatedEvent>(OnCartridgeActivated);
        SubscribeLocalEvent<AppraisalCartridgeComponent, CartridgeDeactivatedEvent>(OnCartridgeDeactivated);
    }

    // Kinda jank, but easiest way to get the right-click Appraisal verb to also work.
    // I'd much rather pass the GetUtilityVerb event through to the AppraisalCartridgeSystem and have all of
    // the functionality in there, rather than adding a PriceGunComponent to the PDA itself, but getting
    // that passthrough to work is not a straightforward thing.

    // Exodus: both clicks and utility verbs now record the completed price gun result in history.

    // Doing this on cartridge activation and deactivation rather than install and remove so that the price
    // gun functionality is only there when the program is active.
    private void OnCartridgeActivated(Entity<AppraisalCartridgeComponent> ent, ref CartridgeActivatedEvent args)
    {
        EnsureComp<PriceGunComponent>(args.Loader);
        // PriceGunSystem methods exit early if a DelayComponent is not present
        EnsureComp<UseDelayComponent>(args.Loader);
    }

    private void OnCartridgeDeactivated(Entity<AppraisalCartridgeComponent> ent, ref CartridgeDeactivatedEvent args)
    {
        var parent = Transform(args.Loader).ParentUid;
        RemComp<PriceGunComponent>(parent);
        RemComp<UseDelayComponent>(parent);
    }

    // Exodus-begin: only successful, throttled scans enter history; the price is calculated once.
    private void OnPriceGunAppraised(Entity<CartridgeLoaderComponent> ent, ref PriceGunAppraisedEvent args)
    {
        if (ent.Comp.ActiveProgram is not { } program ||
            !TryComp<AppraisalCartridgeComponent>(program, out var component) ||
            component.MaxSavedItems <= 0 || Deleted(args.Target))
        {
            return;
        }

        if (component.AppraisedItems.Count >= component.MaxSavedItems)
            component.AppraisedItems.RemoveRange(0, component.AppraisedItems.Count - component.MaxSavedItems + 1);

        component.AppraisedItems.Add(new AppraisedItem(
            Identity.Name(args.Target, EntityManager),
            args.Price.ToString("0.00")));
        UpdateUiState(program, ent.Owner, component);
    }
    // Exodus-end

    /// <summary>
    /// This gets called when the ui fragment needs to be updated for the first time after activating
    /// </summary>
    private void OnUiReady(EntityUid uid, AppraisalCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, component);
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, AppraisalCartridgeComponent? component)
    {
        if (!Resolve(uid, ref component))
            return;

        var state = new AppraisalUiState(component.AppraisedItems);
        _cartridgeLoaderSystem?.UpdateCartridgeUiState(loaderUid, state);
    }
}
