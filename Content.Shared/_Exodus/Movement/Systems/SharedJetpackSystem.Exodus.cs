using Content.Shared.Actions;
using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Physics.Components;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedJetpackSystem
{
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedActionsSystem _actions = default!;

    private void InitializeAutoPause()
    {
        SubscribeLocalEvent<JetpackUserComponent, WeightlessnessChangedEvent>(OnWeightlessnessChanged);
        SubscribeLocalEvent<JetpackUserComponent, EntParentChangedMessage>(OnJetpackUserParentChanged,
            after: new[] { typeof(SharedGravitySystem) });
        SubscribeLocalEvent<JetpackUserComponent, ComponentShutdown>(OnJetpackUserShutdown);
        SubscribeLocalEvent<JetpackComponent, ComponentShutdown>(OnJetpackShutdown);
        SubscribeLocalEvent<JetpackComponent, EntGotRemovedFromContainerMessage>(OnJetpackRemoved);
    }

    private void OnWeightlessnessChanged(Entity<JetpackUserComponent> ent, ref WeightlessnessChangedEvent args)
    {
        RefreshJetpack(ent);
    }

    private void OnJetpackUserParentChanged(Entity<JetpackUserComponent> ent, ref EntParentChangedMessage args)
    {
        // Entering or leaving a vehicle/container can change eligibility without changing weightlessness.
        RefreshJetpack(ent);
    }

    private void OnJetpackUserShutdown(Entity<JetpackUserComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<JetpackComponent>(ent.Comp.Jetpack, out var jetpack) && jetpack.JetpackUser == ent.Owner)
            SetEnabled((ent.Comp.Jetpack, jetpack), false);
    }

    private void OnJetpackShutdown(Entity<JetpackComponent> ent, ref ComponentShutdown args)
    {
        SetEnabled(ent, false);
    }

    private void OnJetpackRemoved(Entity<JetpackComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        SetEnabled(ent, false);
    }

    /// <summary>
    /// Selects a jetpack for automatic flight, or fully disables it. Gravity only pauses flight.
    /// </summary>
    public void SetEnabled(Entity<JetpackComponent> ent, bool enabled, EntityUid? user = null)
    {
        // Fuel is server-side; keep the enabled state and its transitions authoritative as well.
        if (_net.IsClient)
            return;

        if (!enabled)
        {
            DisableJetpack(ent);
            return;
        }

        if (TerminatingOrDeleted(ent) || !ent.Comp.Running ||
            !Container.TryGetContainingContainer((ent.Owner, null, null), out var container))
        {
            return;
        }

        user ??= container.Owner;
        if (user.Value != container.Owner || TerminatingOrDeleted(user.Value) || !CanEnable(ent, user.Value))
            return;

        if (ent.Comp.JetpackUser == user &&
            TryComp<JetpackUserComponent>(user.Value, out var current) &&
            current.Running && current.Jetpack == ent.Owner)
        {
            RefreshJetpack((user.Value, current));
            return;
        }

        if (ent.Comp.JetpackUser != null)
            DisableJetpack(ent);

        // A user can select only one jetpack, including when the previous one is on standby.
        if (TryComp<JetpackUserComponent>(user.Value, out var previous) && previous.Running)
        {
            if (TryComp<JetpackComponent>(previous.Jetpack, out var previousJetpack) &&
                previousJetpack.JetpackUser == user)
            {
                DisableJetpack((previous.Jetpack, previousJetpack));
            }
            else
            {
                RemCompDeferred<JetpackUserComponent>(user.Value);
            }
        }

        var userComp = EnsureComp<JetpackUserComponent>(user.Value);
        userComp.Jetpack = ent.Owner;
        userComp.WeightlessAcceleration = ent.Comp.Acceleration;
        userComp.WeightlessModifier = ent.Comp.WeightlessModifier;
        userComp.WeightlessFriction = ent.Comp.Friction;
        userComp.WeightlessFrictionNoInput = ent.Comp.Friction;
        ent.Comp.JetpackUser = user;
        Dirty(user.Value, userComp);
        Dirty(ent);

        _actions.SetToggled(ent.Comp.ToggleActionEntity, true);
        RefreshJetpack((user.Value, userComp));
    }

    private void RefreshJetpack(Entity<JetpackUserComponent> user)
    {
        if (_net.IsClient || !user.Comp.Running || TerminatingOrDeleted(user))
            return;

        if (!TryComp<JetpackComponent>(user.Comp.Jetpack, out var jetpack) ||
            jetpack.JetpackUser != user.Owner)
        {
            RemCompDeferred<JetpackUserComponent>(user);
            return;
        }

        Entity<JetpackComponent> ent = (user.Comp.Jetpack, jetpack);
        if (TerminatingOrDeleted(ent) || !jetpack.Running ||
            !Container.TryGetContainingContainer((ent.Owner, null, null), out var container) ||
            container.Owner != user.Owner)
        {
            DisableJetpack(ent);
            return;
        }

        var active = _gravity.IsWeightless(user.Owner) &&
                     CanEnableOnGrid(Transform(user).GridUid) &&
                     UserNotParented(user.Owner, jetpack);

        // A tank used for breathing can run dry during standby. Never resume without fuel.
        if (active && !CanEnable(ent, user.Owner))
        {
            DisableJetpack(ent);
            return;
        }

        SetJetpackActive(ent, user, active);
    }

    private void SetJetpackActive(Entity<JetpackComponent> ent, Entity<JetpackUserComponent> user, bool active)
    {
        if (user.Comp.Active == active)
            return;

        user.Comp.Active = active;

        if (active)
            EnsureComp<ActiveJetpackComponent>(ent);
        else
            RemCompDeferred<ActiveJetpackComponent>(ent);

        if (!TerminatingOrDeleted(ent))
            Appearance.SetData(ent, JetpackVisuals.Enabled, active);

        if (TerminatingOrDeleted(user))
            return;

        Dirty(user);

        if (TryComp<PhysicsComponent>(user, out var physics))
            _physics.SetBodyStatus(user, physics, active ? BodyStatus.InAir : BodyStatus.OnGround);

        _movementSpeedModifier.RefreshWeightlessModifiers(user);
        _movementSpeedModifier.RefreshMovementSpeedModifiers(user);
    }

    private void DisableJetpack(Entity<JetpackComponent> ent)
    {
        // Clear the link before shutting down the user component to avoid recursive cleanup.
        var user = ent.Comp.JetpackUser;
        ent.Comp.JetpackUser = null;

        if (user != null && TryComp<JetpackUserComponent>(user.Value, out var userComp) &&
            userComp.Jetpack == ent.Owner)
        {
            SetJetpackActive(ent, (user.Value, userComp), false);

            if (userComp.LifeStage < ComponentLifeStage.Stopping && !TerminatingOrDeleted(user.Value))
                RemCompDeferred<JetpackUserComponent>(user.Value);
        }

        RemCompDeferred<ActiveJetpackComponent>(ent);

        if (TerminatingOrDeleted(ent))
            return;

        Appearance.SetData(ent, JetpackVisuals.Enabled, false);
        _actions.SetToggled(ent.Comp.ToggleActionEntity, false);

        if (user != null)
            Dirty(ent);
    }
}
