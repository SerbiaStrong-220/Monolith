using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Content.Shared.Examine;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Exodus.Stealth.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Shared.Exodus.Stealth;

public abstract class SharedStealthSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<StealthComponent, ComponentGetState>(OnStealthGetState);
        SubscribeLocalEvent<StealthComponent, ComponentHandleState>(OnStealthHandleState);
        SubscribeLocalEvent<StealthComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<StealthComponent, GetVisibilityModifiersEvent>(OnGetVisibilityModifiers);
        SubscribeLocalEvent<StealthComponent, EntityUnpausedEvent>(OnUnpaused);
        SubscribeLocalEvent<StealthComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<StealthComponent, ExamineAttemptEvent>(OnExamineAttempt);
        SubscribeLocalEvent<StealthComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<StealthComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnExamineAttempt(EntityUid uid, StealthComponent component, ExamineAttemptEvent args)
    {
        if (IsVisible(uid))
            return;

        if (!TryGetMinVisibilityData(uid, out var data))
            return;

        if (data != null)
        {
            if (GetVisibility(uid, component) > data.ExamineThreshold)
                return;

            // Don't block examine for owner or children of the cloaked entity.
            // Containers and the like should already block examining, so not bothering to check for occluding containers.
            var source = args.Examiner;
            do
            {
                if (source == uid)
                    return;
                source = Transform(source).ParentUid;
            }
            while (source.IsValid());

            args.Cancel();
        }
    }

    private void OnExamined(EntityUid uid, StealthComponent component, ExaminedEvent args)
    {
        if (!IsVisible(uid))
            return;

        if (!TryGetMinVisibilityData(uid, out var data))
            return;

        if (data != null)
        {
            args.PushMarkup(Loc.GetString(data.ExaminedDesc, ("target", uid)));
        }
    }

    private void OnMobStateChanged(EntityUid uid, StealthComponent component, MobStateChangedEvent args)
    {
        if (IsVisible(uid))
            return;

        if (!TryGetMinVisibilityData(uid, out var data))
            return;

        if (data != null)
        {
            if (args.NewMobState == MobState.Critical && !data.EnabledOnCrit)
            {
                RemCompDeferred<StealthComponent>(uid);
                return;
            }

            if (args.NewMobState == MobState.Dead && !data.EnabledOnDeath)
            {
                RemCompDeferred<StealthComponent>(uid);
                return;
            }

            Dirty(uid, component);
        }
    }

    private void OnUnpaused(EntityUid uid, StealthComponent component, ref EntityUnpausedEvent args)
    {
        component.LastUpdated = _timing.CurTime;
        Dirty(uid, component);
    }

    protected virtual void OnMapInit(EntityUid uid, StealthComponent component, MapInitEvent args)
    {
        if (component.LastUpdated != null || Paused(uid))
            return;

        component.LastUpdated = _timing.CurTime;

        Dirty(uid, component);
    }

    private void OnStealthGetState(EntityUid uid, StealthComponent component, ref ComponentGetState args)
    {
        args.State = new StealthComponentState(component.StealthLayers, component.LastUpdated);
    }

    private void OnStealthHandleState(EntityUid uid, StealthComponent component, ref ComponentHandleState args)
    {
        if (args.Current is not StealthComponentState cast)
            return;

        component.StealthLayers = cast.StealthLayers;

        var ev = new StealthRequestChangeEvent();
        RaiseLocalEvent(uid, ev);

        component.LastUpdated = cast.LastUpdated;
    }

    private void OnMove(EntityUid uid, StealthComponent component, ref MoveEvent args)
    {
        if (TerminatingOrDeleted(uid) || IsVisible(uid))
            return;

        if (!TryGetMinVisibilityData(uid, out var data))
            return;

        if (data != null)
        {
            if (_timing.ApplyingState)
                return;

            if (args.NewPosition.EntityId != args.OldPosition.EntityId)
                return;

            var delta = data.MovementVisibilityRate * (args.NewPosition.Position - args.OldPosition.Position).Length();

            ModifyVisibility(uid, delta);
        }
    }

    private void OnGetVisibilityModifiers(EntityUid uid, StealthComponent component, GetVisibilityModifiersEvent args)
    {
        if (IsVisible(uid))
            return;

        if (!TryGetMinVisibilityData(uid, out var data))
            return;

        if (data != null)
        {
            var limit = data.MinVisibility;
            if (TryComp<PhysicsComponent>(uid, out var phys))
                limit += Math.Min(data.MaxInvisibilityPenalty, phys.LinearVelocity.Length() * data.InvisibilityPenalty);

            if (data.LastVisibility > limit)
                args.FlatModifier += args.SecondsSinceUpdate * data.PassiveVisibilityRate;
        }
    }

    /// <summary>
    /// /// Modifies the visibility based on the delta provided.
    /// </summary>
    /// <param name="delta">The delta to be used in visibility calculation.</param>
    public void ModifyVisibility(EntityUid uid, float delta, StealthComponent? component = null)
    {
        if (delta == 0 || !Resolve(uid, ref component) || component.StealthLayers.Count == 0)
            return;

        if (!TryGetMinVisibilityData(uid, out var data))
            return;

        if (data != null)
        {
            if (component.LastUpdated != null)
            {
                data.LastVisibility = GetVisibility(uid, component);
                component.LastUpdated = _timing.CurTime;
            }

            data.LastVisibility = Math.Clamp(data.LastVisibility + delta, data.MinVisibility, data.MaxVisibility);
            Dirty(uid, component);
        }
    }

    /// <summary>
    /// Sets the visibility directly with no modifications
    /// </summary>
    /// <param name="value">The value to set the visibility to. -1 is fully invisible, 1 is fully visible</param>
    public void SetVisibility(EntityUid uid, float value, StealthComponent? component = null)
    {
        if (!Resolve(uid, ref component) || IsVisible(uid))
            return;

        if (!TryGetMinVisibilityData(uid, out var data))
            return;

        if (data != null)
        {
            data.LastVisibility = Math.Clamp(value, data.MinVisibility, data.MaxVisibility);
            if (component.LastUpdated != null)
                component.LastUpdated = _timing.CurTime;

            Dirty(uid, component);
        }
    }

    /// <summary>
    /// Gets the current visibility from the <see cref="StealthComponent"/>
    /// Use this instead of getting LastVisibility from the component directly.
    /// </summary>
    /// <returns>Returns a calculation that accounts for any stealth change that happened since last update, otherwise
    /// returns based on if it can resolve the component. Note that the returned value may be larger than the components
    /// maximum stealth value if it is currently disabled.</returns>
    public float GetVisibility(EntityUid uid, StealthComponent? component = null)
    {
        if (!Resolve(uid, ref component) || TerminatingOrDeleted(uid) || IsVisible(uid))
            return 1;

        if (!TryGetMinVisibilityData(uid, out var data))
            return 1;

        if (data != null)
        {
            if (component.LastUpdated == null)
                return data.LastVisibility;

            var deltaTime = _timing.CurTime - component.LastUpdated.Value;

            var ev = new GetVisibilityModifiersEvent(uid, component, (float)deltaTime.TotalSeconds, 0f);
            RaiseLocalEvent(uid, ev, false);

            return Math.Clamp(data.LastVisibility + ev.FlatModifier, data.MinVisibility, data.MaxVisibility);
        }

        return 1;
    }

    public bool RequestStealth(EntityUid target, string key, StealthData data)
    {
        if (!Exists(target) || TerminatingOrDeleted(target))
            return false;

        var stealthComp = EnsureComp<StealthComponent>(target);

        if (!stealthComp.StealthLayers.ContainsKey(key))
            stealthComp.StealthLayers.Add(key, data);

        Dirty(target, stealthComp);

        return true;
    }

    public bool RemoveRequest(string key, EntityUid target)
    {
        if (!Exists(target) || TerminatingOrDeleted(target))
            return false;

        if (!TryComp<StealthComponent>(target, out var stealthComp))
            return false;

        if (!stealthComp.StealthLayers.ContainsKey(key))
            return false;

        stealthComp.StealthLayers.Remove(key);

        if (IsVisible(target))
        {
            RemCompDeferred<StealthComponent>(target);
            return true;
        }

        Dirty(target, stealthComp);

        return true;
    }

    public bool TryGetMinVisibilityData(EntityUid uid, [NotNullWhen(true)] out StealthData? returnData, StealthComponent? component = null)
    {
        returnData = null;
        var minValue = float.MaxValue;

        if (!Resolve(uid, ref component) || TerminatingOrDeleted(uid) || IsVisible(uid))
            return false;

        foreach (var data in component.StealthLayers.Values)
        {
            var clamped = Math.Clamp(data.LastVisibility, data.MinVisibility, data.MaxVisibility);

            if (clamped < minValue)
            {
                minValue = clamped;
                returnData = data;
            }
        }

        if (returnData == null)
            return false;

        return true;
    }

    public bool IsVisible(EntityUid uid)
    {
        if (!TryComp<StealthComponent>(uid, out var stealthComp))
            return true;

        return stealthComp.StealthLayers.Count == 0;
    }

    /// <summary>
    ///     Used to run through any stealth effecting components on the entity.
    /// </summary>
    private sealed class GetVisibilityModifiersEvent : EntityEventArgs
    {
        public readonly StealthComponent Stealth;
        public readonly float SecondsSinceUpdate;

        /// <summary>
        ///     Calculate this and add to it. Do not divide, multiply, or overwrite.
        ///     The sum will be added to the stealth component's visibility.
        /// </summary>
        public float FlatModifier;

        public GetVisibilityModifiersEvent(EntityUid uid, StealthComponent stealth, float secondsSinceUpdate, float flatModifier)
        {
            Stealth = stealth;
            SecondsSinceUpdate = secondsSinceUpdate;
            FlatModifier = flatModifier;
        }
    }
}

public sealed class StealthRequestChangeEvent : EntityEventArgs;
