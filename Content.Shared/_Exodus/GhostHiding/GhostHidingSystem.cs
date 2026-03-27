using Content.Shared.Actions;
using Content.Shared.Popups;

namespace Content.Shared._Exodus.GhostHiding;

public sealed partial class GhostHidingSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedVisibilitySystem _visibility = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GhostHidingComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<GhostHidingComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GhostHidingComponent, ToggleGhostHidingActionEvent>(OnToggle);
        SubscribeLocalEvent<GhostHidingComponent, GetVisMaskEvent>(OnGetEyeMask);
    }

    private void OnInit(Entity<GhostHidingComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.ActionUid = _actions.AddAction(ent, ent.Comp.Action);
    }

    private void OnShutdown(Entity<GhostHidingComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.ActionUid != null)
        {
            _actions.RemoveAction(ent, ent.Comp.ActionUid);
        }
    }

    private void OnGetEyeMask(Entity<GhostHidingComponent> ent, ref GetVisMaskEvent args)
    {
        if (ent.Comp.Hiding)
        {
            args.VisibilityMask |= (int)ent.Comp.HidingVisibilityMask;
        }
    }

    private void OnToggle(Entity<GhostHidingComponent> ent, ref ToggleGhostHidingActionEvent args)
    {
        var (uid, comp) = ent;

        comp.Hiding = !comp.Hiding;
        _popup.PopupClient(comp.Hiding ? comp.HiddenPopup : comp.NotHiddenPopup, ent, ent);

        _visibility.SetLayer(uid, (ushort)(comp.Hiding ? comp.HidingVisibilityLayers : comp.BaseVisibilityLayers), refresh: true);
        _eye.RefreshVisibilityMask(uid);
    }
}
