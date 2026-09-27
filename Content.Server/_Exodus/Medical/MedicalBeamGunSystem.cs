using System.Numerics;
using Content.Server._Exodus.Visuals;
using Content.Server.PowerCell;
using Content.Shared._Exodus.Medical;
using Content.Shared._Exodus.Visuals;
using Content.Shared._Shitmed.Targeting;
using Content.Shared.ActionBlocker;
using Content.Shared.CombatMode;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Medical;

public sealed partial class MedicalBeamGunSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private EntityLinkVisualSystem _links = default!;
    [Dependency] private PowerCellSystem _cells = default!;

    private EntityQuery<HandsComponent> _handsQuery;
    private EntityQuery<CombatModeComponent> _combatQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<DamageableComponent> _damageQuery;
    private EntityQuery<MedicalBeamActiveComponent> _activeQuery;
    private EntityQuery<MedicalBeamPatientComponent> _patientQuery;

    public override void Initialize()
    {
        base.Initialize();
        _handsQuery = GetEntityQuery<HandsComponent>();
        _combatQuery = GetEntityQuery<CombatModeComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _damageQuery = GetEntityQuery<DamageableComponent>();
        _activeQuery = GetEntityQuery<MedicalBeamActiveComponent>();
        _patientQuery = GetEntityQuery<MedicalBeamPatientComponent>();

        SubscribeNetworkEvent<MedicalBeamGunInputEvent>(OnInput);
        SubscribeLocalEvent<MedicalBeamGunComponent, HandDeselectedEvent>(OnDeselected);
        SubscribeLocalEvent<MedicalBeamGunComponent, GotUnequippedHandEvent>(OnUnequipped);
        SubscribeLocalEvent<MedicalBeamGunComponent, ComponentShutdown>(OnGunShutdown);
        SubscribeLocalEvent<MedicalBeamGunComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<MedicalBeamActiveComponent, ComponentShutdown>(OnActiveShutdown);
    }

    private void OnInput(MedicalBeamGunInputEvent message, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } user ||
            !TryGetEntity(message.Gun, out var gunUid) || gunUid is not { } gun ||
            TerminatingOrDeleted(gun) || !TryComp<MedicalBeamGunComponent>(gun, out var component))
            return;

        if (message.Target is not { } netTarget || !TryGetEntity(netTarget, out var target) || target is not { } patient)
        {
            if (_activeQuery.TryComp(gun, out var active) && active.User == user)
                StopHealing((gun, active));
            return;
        }

        TrySetTarget((gun, component), user, patient);
    }

    /// <summary>Accepts an authenticated held input. Treatment is validated again on each pulse.</summary>
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
               _handsQuery.TryComp(user, out var hands) && hands.ActiveHandEntity == gun.Owner &&
               _combatQuery.TryComp(user, out var combat) && combat.IsInCombatMode &&
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
            if (now >= active.InputExpires || TerminatingOrDeleted(active.User) || TerminatingOrDeleted(active.Target))
            {
                StopHealing(beam);
                continue;
            }
            if (now < active.NextCheck)
                continue;
            active.NextCheck += gun.HealInterval;
            if (active.NextCheck <= now)
                active.NextCheck = now + gun.HealInterval;

            if (!CanTreat((uid, gun), active.User, active.Target) || !HasClearBeam(active.User, active.Target, gun.Range) ||
                !_damageQuery.TryComp(active.Target, out var damageable))
            {
                StopHealing(beam);
                continue;
            }

            var charge = Math.Max(0f, gun.ChargePerSecond) * (float) gun.HealInterval.TotalSeconds;
            if (charge > 0f && !_cells.HasCharge(uid, charge))
            {
                StopHealing(beam);
                continue;
            }

            var patient = EnsureComp<MedicalBeamPatientComponent>(active.Target);
            if (patient.Gun is { } other && other != uid && _activeQuery.TryComp(other, out var otherBeam) &&
                otherBeam.Running && otherBeam.Target == active.Target && now < otherBeam.InputExpires)
            {
                // Wait for the current medic to release the patient; no duplicate healing or cell drain.
                ClearVisual(uid);
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
            if (now < active.NextHeal)
                continue;

            active.NextHeal += gun.HealInterval;
            if (active.NextHeal <= now)
                active.NextHeal = now + gun.HealInterval;
            patient.NextHeal = active.NextHeal;

            var healing = active.Healing;
            healing.DamageDict.Clear();
            foreach (var (groupId, rate) in gun.GroupHealing)
            {
                if (_prototype.TryIndex(groupId, out var group))
                    AddGroupHealing(damageable.Damage, group.DamageTypes, rate * gun.HealInterval.TotalSeconds, healing);
            }
            foreach (var (type, rate) in gun.TypeHealing)
                AddTypeHealing(damageable.Damage, type.Id, rate * gun.HealInterval.TotalSeconds, healing);

            // A healthy patient can remain targeted without consuming energy.
            if (healing.Empty)
                continue;
            if (charge > 0f && !_cells.TryUseCharge(uid, charge))
            {
                StopHealing(beam);
                continue;
            }

            _damage.TryChangeDamage(active.Target, healing, ignoreResistances: true, interruptsDoAfters: false,
                damageable: damageable, origin: active.User, targetPart: TargetBodyPart.All, canSever: false, tool: uid);
        }
    }

    private bool HasClearBeam(EntityUid user, EntityUid target, float range)
    {
        var from = _transform.GetMapCoordinates(user);
        var to = _transform.GetMapCoordinates(target);
        return from.MapId == to.MapId && Vector2.DistanceSquared(from.Position, to.Position) <= range * range &&
               _interaction.InRangeUnobstructed(user, target, range, overlapCheck: false);
    }

    private void StopHealing(Entity<MedicalBeamActiveComponent> beam)
    {
        ClearVisual(beam);
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
        if (_patientQuery.TryComp(ent.Comp.Target, out var patient) && patient.Gun == ent.Owner)
            patient.Gun = null;
    }

    private void OnDeselected(Entity<MedicalBeamGunComponent> ent, ref HandDeselectedEvent args)
    {
        if (_activeQuery.TryComp(ent, out var active))
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
        if (ent.Comp.ChargePerSecond <= 0f)
            return;
        args.PushMarkup(_cells.TryGetBatteryFromSlot(ent, out var battery)
            ? Loc.GetString("medical-beam-gun-charge", ("seconds", (int) (battery.CurrentCharge / ent.Comp.ChargePerSecond)))
            : Loc.GetString("medical-beam-gun-no-cell"));
    }
}
