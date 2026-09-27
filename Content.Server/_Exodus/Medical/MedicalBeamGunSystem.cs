using Content.Server._Exodus.Visuals;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.PowerCell;
using Content.Shared._Exodus.Medical;
using Content.Shared._Exodus.Visuals;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Emp;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Medical;

public sealed partial class MedicalBeamGunSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private EntityLinkVisualSystem _links = default!;
    [Dependency] private PowerCellSystem _cells = default!;

    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<CombatModeComponent> _combatQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<DamageableComponent> _damageQuery;
    private EntityQuery<BloodstreamComponent> _bloodstreamQuery;
    private EntityQuery<MedicalBeamActiveComponent> _activeQuery;
    private EntityQuery<MedicalBeamPatientComponent> _patientQuery;
    private EntityQuery<RequireProjectileTargetComponent> _projectileTargetQuery;

    public override void Initialize()
    {
        base.Initialize();
        _handsQuery = GetEntityQuery<HandsComponent>();
        _combatQuery = GetEntityQuery<CombatModeComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _damageQuery = GetEntityQuery<DamageableComponent>();
        _bloodstreamQuery = GetEntityQuery<BloodstreamComponent>();
        _activeQuery = GetEntityQuery<MedicalBeamActiveComponent>();
        _patientQuery = GetEntityQuery<MedicalBeamPatientComponent>();
        _projectileTargetQuery = GetEntityQuery<RequireProjectileTargetComponent>();

        SubscribeNetworkEvent<MedicalBeamGunInputEvent>(OnInput);
        SubscribeLocalEvent<MedicalBeamGunComponent, HandDeselectedEvent>(OnDeselected);
        SubscribeLocalEvent<MedicalBeamGunComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<MedicalBeamGunComponent, ComponentShutdown>(OnGunShutdown);
        SubscribeLocalEvent<MedicalBeamGunComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MedicalBeamActiveComponent, ComponentShutdown>(OnActiveShutdown);
        SubscribeLocalEvent<MedicalBeamGunComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<MedicalBeamGunComponent, GetVerbsEvent<AlternativeVerb>>(OnModeVerb);
        SubscribeLocalEvent<HandsComponent, PlayerDetachedEvent>(OnPlayerDetached);
    }

    private void OnInput(MedicalBeamGunInputEvent message, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !TryGetEntity(message.Gun, out var gunUid) || gunUid is not { } gun ||
            TerminatingOrDeleted(gun) || !TryComp<MedicalBeamGunComponent>(gun, out var component))
            return;

        EntityUid? target = null;
        if (message.Target is { } netTarget)
            TryGetEntity(netTarget, out target);
        TryHandleInput((gun, component), user, target, message.Mode);
    }

    internal bool TryHandleInput(Entity<MedicalBeamGunComponent> gun, EntityUid user, EntityUid? target, MedicalBeamMode mode)
    {
        // Ignore stale heartbeats/releases from the previous mode after the selector has changed.
        if (gun.Comp.Mode != mode || !_handsQuery.TryComp(user, out var hands) || hands.ActiveHandEntity != gun.Owner)
            return false;

        if (target == null)
        {
            if (_activeQuery.TryComp(gun, out var active) && active.User == user)
                StopHealing((gun, active));
            return true;
        }

        if (mode == MedicalBeamMode.Automatic && _activeQuery.TryComp(gun, out var previous) &&
            previous.Running && previous.User == user && previous.Target == target)
        {
            StopHealing((gun, previous));
            return true;
        }
        return TrySetTarget(gun, user, target.Value);
    }

    /// <summary>Accepts an authenticated target request. Treatment is validated again on each pulse.</summary>
    internal bool TrySetTarget(Entity<MedicalBeamGunComponent> gun, EntityUid user, EntityUid target)
    {
        if (!CanTreat(gun, user, target))
        {
            if (_activeQuery.TryComp(gun, out var previous) && previous.User == user)
                StopHealing((gun, previous));
            return false;
        }

        if (_activeQuery.TryComp(gun, out var active) && active.Running)
        {
            if (active.User == user && active.Target == target)
            {
                active.InputExpires = _timing.CurTime + gun.Comp.InputTimeout;
                return true;
            }
            StopHealing((gun, active));
        }

        active = EnsureComp<MedicalBeamActiveComponent>(gun);
        active.User = user;
        active.Target = target;
        active.Mode = gun.Comp.Mode;
        active.InputExpires = _timing.CurTime + gun.Comp.InputTimeout;
        active.NextCheck = _timing.CurTime;
        // No instant healing on click: switching patients or tapping cannot bypass the rate limit.
        active.NextHeal = _timing.CurTime + gun.Comp.HealInterval;
        return true;
    }

    private bool CanTreat(Entity<MedicalBeamGunComponent> gun, EntityUid user, EntityUid target)
    {
        return user != target && !TerminatingOrDeleted(gun) && !TerminatingOrDeleted(user) &&
               !TerminatingOrDeleted(target) && !Paused(gun) && !Paused(user) && !Paused(target) &&
               gun.Comp.Range > 0f && gun.Comp.HealInterval > TimeSpan.Zero && gun.Comp.InputTimeout > TimeSpan.Zero &&
               _handsQuery.TryComp(user, out var hands) &&
               (hands.ActiveHandEntity == gun.Owner ||
                gun.Comp.Mode == MedicalBeamMode.Automatic && _hands.IsHolding((user, hands), gun.Owner)) &&
               (gun.Comp.Mode == MedicalBeamMode.Automatic ||
                _combatQuery.TryComp(user, out var combat) && combat.IsInCombatMode) &&
               _mobQuery.TryComp(user, out var medic) && medic.CurrentState == MobState.Alive &&
               _mobQuery.TryComp(target, out var patient) && patient.CurrentState != MobState.Dead &&
               _damageQuery.HasComp(target) && !HasComp<EmpDisabledComponent>(gun) &&
               !_containers.IsEntityOrParentInContainer(user) && !_containers.IsEntityOrParentInContainer(target) &&
               _whitelist.CheckBoth(target, whitelist: gun.Comp.TargetWhitelist) &&
               _blocker.CanInteract(user, target) && _blocker.CanUseHeldEntity(user, gun);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MedicalBeamActiveComponent, MedicalBeamGunComponent>();
        while (query.MoveNext(out var uid, out var active, out var gun))
        {
            if (!active.Running)
                continue;
            Entity<MedicalBeamActiveComponent> beam = (uid, active);
            if (active.Mode != gun.Mode ||
                active.Mode == MedicalBeamMode.Manual && now >= active.InputExpires ||
                TerminatingOrDeleted(active.User) || TerminatingOrDeleted(active.Target))
            {
                StopHealing(beam);
                continue;
            }
            if (now < active.NextCheck)
                continue;
            active.NextCheck += gun.HealInterval;
            if (active.NextCheck <= now)
                active.NextCheck = now + gun.HealInterval;

            if (!CanTreat((uid, gun), active.User, active.Target) || !HasClearBeam(active.User, active.Target, gun) ||
                !_damageQuery.TryComp(active.Target, out var damageable))
            {
                StopHealing(beam);
                continue;
            }

            var charge = GetChargeRate(gun) * (float) gun.HealInterval.TotalSeconds;
            if (charge > 0f && !_cells.HasCharge(uid, charge))
            {
                StopHealing(beam);
                continue;
            }

            var patient = EnsureComp<MedicalBeamPatientComponent>(active.Target);
            if (patient.Gun is { } other && other != uid && _activeQuery.TryComp(other, out var otherBeam) &&
                otherBeam.Running && otherBeam.Target == active.Target &&
                (otherBeam.Mode == MedicalBeamMode.Automatic || now < otherBeam.InputExpires))
            {
                // Wait for the current medic to release the patient; no duplicate healing or cell drain.
                ClearVisual(uid);
                StopTreatmentSound(beam);
                continue;
            }

            if (patient.Gun != uid)
            {
                patient.Gun = uid;
                if (active.NextHeal < patient.NextHeal)
                    active.NextHeal = patient.NextHeal;
            }
            if (!_links.TrySetLink(uid, active.Target, gun.BeamStyle))
            {
                StopHealing(beam);
                continue;
            }
            StartTreatmentSound(beam, gun);
            if (now < active.NextHeal)
                continue;

            active.NextHeal += gun.HealInterval;
            if (active.NextHeal <= now)
                active.NextHeal = now + gun.HealInterval;
            patient.NextHeal = active.NextHeal;

            // Maintaining the beam costs power even if the patient has no injuries.
            if (charge > 0f && !_cells.TryUseCharge(uid, charge))
            {
                StopHealing(beam);
                continue;
            }

            var healing = active.Healing;
            healing.DamageDict.Clear();
            foreach (var (groupId, rate) in gun.GroupHealing)
            {
                if (_prototype.TryIndex(groupId, out var group))
                    AddGroupHealing(damageable.Damage, group.DamageTypes, GetHealingRate(gun, rate) * gun.HealInterval.TotalSeconds, healing);
            }
            foreach (var (type, rate) in gun.TypeHealing)
                AddTypeHealing(damageable.Damage, type.Id, GetHealingRate(gun, rate) * gun.HealInterval.TotalSeconds, healing);

            if (gun.BleedReductionPerSecond > 0f &&
                _bloodstreamQuery.TryComp(active.Target, out var blood) && blood.BleedAmount > 0f)
            {
                var reduction = gun.BleedReductionPerSecond * (float) gun.HealInterval.TotalSeconds;
                if (gun.Mode == MedicalBeamMode.Automatic)
                    reduction /= Math.Max(1f, gun.AutomaticRateDivisor);
                _bloodstream.TryModifyBleedAmount(active.Target, -reduction, blood);
            }

            if (!healing.Empty)
            {
                _damage.TryChangeDamage(active.Target, healing, ignoreResistances: true, interruptsDoAfters: false,
                    damageable: damageable, origin: active.User, targetPart: TargetBodyPart.All, canSever: false, tool: uid);
            }
        }
    }

    private bool HasClearBeam(EntityUid user, EntityUid target, MedicalBeamGunComponent gun)
    {
        var from = _transform.GetMapCoordinates(user);
        var to = _transform.GetMapCoordinates(target);
        var difference = to.Position - from.Position;
        var distance = difference.Length();
        if (from.MapId != to.MapId || distance > gun.Range)
            return false;
        if (MathHelper.CloseTo(distance, 0f))
            return true;

        var ray = new CollisionRay(from.Position, difference / distance, (int) gun.CollisionMask);
        var state = (User: user, Target: target, Query: _projectileTargetQuery);
        var hits = _physics.IntersectRayWithPredicate(from.MapId, ray, state,
            static (hit, context) => hit == context.User || hit == context.Target ||
                                    context.Query.TryComp(hit, out var selective) && selective.Active,
            distance);
        foreach (var _ in hits)
            return false;
        return true;
    }

    private void StopHealing(Entity<MedicalBeamActiveComponent> beam)
    {
        ClearVisual(beam);
        StopTreatmentSound(beam);
        if (_patientQuery.TryComp(beam.Comp.Target, out var patient) && patient.Gun == beam.Owner)
            patient.Gun = null;
        RemCompDeferred<MedicalBeamActiveComponent>(beam);
    }

    private void ClearVisual(EntityUid gun)
    {
        if (!TerminatingOrDeleted(gun) && TryComp<EntityLinkVisualComponent>(gun, out var link))
            _links.ClearLink((gun, link));
    }

    private void OnActiveShutdown(Entity<MedicalBeamActiveComponent> ent, ref ComponentShutdown args)
    {
        ClearVisual(ent);
        StopTreatmentSound(ent);
        if (_patientQuery.TryComp(ent.Comp.Target, out var patient) && patient.Gun == ent.Owner)
            patient.Gun = null;
    }

    private void OnDeselected(Entity<MedicalBeamGunComponent> ent, ref HandDeselectedEvent args)
    {
        if (_activeQuery.TryComp(ent, out var active) && active.Mode == MedicalBeamMode.Manual)
            StopHealing((ent, active));
    }

    private void OnUnequipped(Entity<MedicalBeamGunComponent> ent, ref GotUnequippedHandEvent args)
    {
        if (_activeQuery.TryComp(ent, out var active))
            StopHealing((ent, active));
    }

    private void OnGunShutdown(Entity<MedicalBeamGunComponent> ent, ref ComponentShutdown args)
    {
        if (_activeQuery.TryComp(ent, out var active))
            StopHealing((ent, active));
    }

    private void OnExamined(Entity<MedicalBeamGunComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("medical-beam-gun-examine", ("range", ent.Comp.Range)));
        args.PushMarkup(Loc.GetString("medical-beam-gun-mode", ("mode", GetModeName(ent.Comp.Mode))));
        args.PushMarkup(Loc.GetString(ent.Comp.Mode == MedicalBeamMode.Automatic
            ? "medical-beam-gun-controls-automatic"
            : "medical-beam-gun-controls-manual"));
        var chargeRate = GetChargeRate(ent.Comp);
        if (chargeRate <= 0f)
            return;
        args.PushMarkup(_cells.TryGetBatteryFromSlot(ent, out var battery)
            ? Loc.GetString("medical-beam-gun-charge", ("seconds", (int) (battery.CurrentCharge / chargeRate)))
            : Loc.GetString("medical-beam-gun-no-cell"));
    }

    private void OnPlayerDetached(Entity<HandsComponent> ent, ref PlayerDetachedEvent args)
    {
        foreach (var hand in ent.Comp.Hands.Values)
        {
            if (hand.HeldEntity is { } gun && _activeQuery.TryComp(gun, out var active) && active.User == ent.Owner)
                StopHealing((gun, active));
        }
    }
}
