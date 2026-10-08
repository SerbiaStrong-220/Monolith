using Content.Shared.Actions;
using Content.Shared._EE.CCVar; // EE
using Content.Shared.Gravity;
using Content.Shared.Interaction.Events;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Robust.Shared.Configuration; // EE
using Robust.Shared.Containers;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Serialization;
// Exodus: auto-pause and lifecycle dependencies live in SharedJetpackSystem.Exodus.cs.

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedJetpackSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movementSpeedModifier = default!;
    [Dependency] protected SharedAppearanceSystem Appearance = default!;
    [Dependency] protected SharedContainerSystem Container = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private ActionContainerSystem _actionContainer = default!;
    [Dependency] private IConfigurationManager _config = default!; // EE
    [Dependency] private SharedGravitySystem _gravity = default!; // Mono

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<JetpackComponent, GetItemActionsEvent>(OnJetpackGetAction);
        SubscribeLocalEvent<JetpackComponent, DroppedEvent>(OnJetpackDropped);
        SubscribeLocalEvent<JetpackComponent, ToggleJetpackEvent>(OnJetpackToggle);

        SubscribeLocalEvent<JetpackUserComponent, RefreshWeightlessModifiersEvent>(OnJetpackUserWeightlessMovement);
        SubscribeLocalEvent<JetpackUserComponent, CanWeightlessMoveEvent>(OnJetpackUserCanWeightless);
        SubscribeLocalEvent<JetpackComponent, EntGotInsertedIntoContainerMessage>(OnJetpackMoved);

        InitializeAutoPause(); // Exodus: watch the user's weightlessness instead of disabling jetpacks grid-wide.
        SubscribeLocalEvent<JetpackComponent, MapInitEvent>(OnMapInit);
    }

    private void OnJetpackUserWeightlessMovement(Entity<JetpackUserComponent> ent, ref RefreshWeightlessModifiersEvent args)
    {
        if (!ent.Comp.Active) // Exodus: standby must not change movement.
            return;

        // Yes this bulldozes the values but primarily for backwards compat atm.
        args.WeightlessAcceleration = ent.Comp.WeightlessAcceleration;
        args.WeightlessModifier = ent.Comp.WeightlessModifier;
        args.WeightlessFriction = ent.Comp.WeightlessFriction;
        args.WeightlessFrictionNoInput = ent.Comp.WeightlessFrictionNoInput;
    }

    private void OnMapInit(EntityUid uid, JetpackComponent component, MapInitEvent args)
    {
        _actionContainer.EnsureAction(uid, ref component.ToggleActionEntity, component.ToggleAction);
        Dirty(uid, component);
    }

    // Exodus-begin: lifecycle handlers use the persistent enabled state.
    private void OnJetpackDropped(Entity<JetpackComponent> ent, ref DroppedEvent args)
    {
        SetEnabled(ent, false);
    }

    private void OnJetpackMoved(Entity<JetpackComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        if (args.Container.Owner != ent.Comp.JetpackUser)
            SetEnabled(ent, false);
    }

    private void OnJetpackUserCanWeightless(Entity<JetpackUserComponent> ent, ref CanWeightlessMoveEvent args)
    {
        if (ent.Comp.Active)
            args.CanMove = true;
    }

    private void OnJetpackToggle(Entity<JetpackComponent> ent, ref ToggleJetpackEvent args)
    {
        if (args.Handled)
            return;

        SetEnabled(ent, ent.Comp.JetpackUser == null, args.Performer);
        args.Handled = true;
    }
    // Exodus-end

    private bool CanEnableOnGrid(EntityUid? gridUid)
    {
        // No and no again! Do not attempt to activate the jetpack on a grid with gravity disabled. You will not be the first or the last to try this.
        // https://discord.com/channels/310555209753690112/310555209753690112/1270067921682694234
        return gridUid == null // EE
        //||(!HasComp<GravityComponent>(gridUid)); // EE
            || _config.GetCVar(EECCVars.JetpackEnableAnywhere) // EE
            || _config.GetCVar(EECCVars.JetpackEnableInNoGravity) // EE
            && TryComp<GravityComponent>(gridUid, out var comp) // EE
            && !comp.Enabled; // EE
    }

    private void OnJetpackGetAction(EntityUid uid, JetpackComponent component, GetItemActionsEvent args)
    {
        args.AddAction(ref component.ToggleActionEntity, component.ToggleAction);
    }

    // Exodus-begin: standby users are not flying; enabling no longer requires weightlessness.
    // State transitions and cleanup are implemented in SharedJetpackSystem.Exodus.cs.
    public bool IsUserFlying(EntityUid uid)
    {
        return TryComp<JetpackUserComponent>(uid, out var user) && user.Active;
    }

    protected virtual bool CanEnable(Entity<JetpackComponent> ent, EntityUid user)
    {
        return true;
    }
    // Exodus-end

    // EE: check parent
    protected virtual bool UserNotParented(EntityUid? user, JetpackComponent component)
    {
        return !TryComp(user, out TransformComponent? xform)
            || xform.ParentUid == xform.GridUid
            || xform.ParentUid == xform.MapUid;
    }
    // End EE

    // Exodus: magboots now pause/resume through WeightlessnessChangedEvent.
}

[Serializable, NetSerializable]
public enum JetpackVisuals : byte
{
    Enabled,
}
