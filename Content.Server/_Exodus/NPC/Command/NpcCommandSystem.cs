using System.Linq;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server._Exodus.NPC.Abilities;
using Content.Shared.Chat;
using Content.Shared._Exodus.NPC.Command;
using Content.Shared.Damage;
using Content.Shared.Dataset;
using Content.Shared.Mobs.Systems;
using Content.Shared.NPC.Systems;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.NPC.Command;

/// So this is squad manager. Here we recruit NPCs, define what orders is, giving orders, define what team can do.
/// Behaviour itself is in the HTN trees. This just decides and relays.
public sealed class NpcCommandSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _xform = default!;
    [Dependency] private NpcFactionSystem _faction = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _thresholds = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    // Reused per-tick recruitment scratch buffers, so we don't allocate two Lists every commander update.
    private readonly List<(EntityUid Ent, float Dist)> _medics = new();
    private readonly List<(EntityUid Ent, float Dist)> _grunts = new();

    // Blackboard key carrying the assault target's last-known position to grunts.
    private const string OrderedTargetCoordinatesKey = "OrderedTargetCoordinates";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcCommanderComponent, ComponentShutdown>(OnCommanderShutdown);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<NpcCommanderComponent>();
        while (query.MoveNext(out var uid, out var commander))
        {
            if (now < commander.NextUpdate)
                continue;

            commander.NextUpdate = now + commander.UpdateInterval;
            UpdateCommander((uid, commander));
        }
    }

    private void UpdateCommander(Entity<NpcCommanderComponent> ent)
    {
        var c = ent.Comp;
        var now = _timing.CurTime;

        // If commander downed/dead disband so members revert to solo combat instead of obeying a corpse.
        if (_mobState.IsIncapacitated(ent.Owner))
        {
            if (c.Retinue.Count > 0)
                DisbandSquad(ent);
            return;
        }

        // Recruitment within slots
        var coords = _xform.GetMapCoordinates(ent.Owner);
        _medics.Clear();
        _grunts.Clear();

        // Scans KeepRange so existing members aren't loose, new ones still recruit within range.
        foreach (var minion in _lookup.GetEntitiesInRange<NpcMinionComponent>(coords, c.KeepRange))
        {
            if (minion.Owner == ent.Owner || !_faction.IsEntityFriendly(ent.Owner, minion.Owner))
                continue;

            if (minion.Comp.Commander is { } other && other != ent.Owner && !Deleted(other) && HasComp<NpcCommanderComponent>(other))
                continue;

            var dist = (coords.Position - _xform.GetMapCoordinates(minion.Owner).Position).Length();

            // Pull in new NPCs that are within recruit range.
            if (dist > c.Range && !c.Retinue.Contains(minion.Owner))
                continue;

            (minion.Comp.Medic ? _medics : _grunts).Add((minion.Owner, dist));
        }

        var chosenGrunts = PickSlots(_grunts, c.Retinue, c.MaxGrunts).ToList();
        var chosen = new HashSet<EntityUid>(PickSlots(_medics, c.Retinue, c.MaxMedics));
        chosen.UnionWith(chosenGrunts);

        // Tracks active grunts so a downed member still counts as a loss even if dragged out of range.
        foreach (var g in chosenGrunts)
            c.Members.Add(g);
        c.Members.RemoveWhere(m => Deleted(m) || (!chosen.Contains(m) && !_mobState.IsCritical(m) && !_mobState.IsDead(m)));

        var reduced = false;
        foreach (var m in c.Members)
        {
            if (!_mobState.IsCritical(m) && !_mobState.IsDead(m))
                continue;

            reduced = true;
            break;
        }

        // States
        var enemy = GetEnemy(ent);
        var hp = HealthFraction(ent.Owner);
        var newOrder = DecideOrder(c, now, enemy, hp, reduced);

        // Give order voiceline, but only when it actually changes (no spam).
        if (newOrder != c.Order)
            Announce(ent.Owner, c, newOrder);

        c.Order = newOrder;

        // Remember where the assault target is while coma can see it and use later. Cleared afterwards.
        if (c.Order == NpcOrder.Attack)
        {
            if (c.AssaultTarget is { } at && !Deleted(at) && !_mobState.IsDead(at))
                c.AssaultTargetCoordinates = Transform(at).Coordinates;
        }
        else
        {
            c.AssaultTargetCoordinates = null;
        }

        // Orders and stance: coma fights/patrols on Follow, and only retreats/holds if orders.
        SetCurrentOrder(ent.Owner, c.Order is NpcOrder.Retreat or NpcOrder.Hold ? c.Order : NpcOrder.Follow, null, ent.Owner);

        foreach (var minion in chosen)
        {
            if (!TryComp<NpcMinionComponent>(minion, out var comp))
                continue;

            comp.Commander = ent.Owner;

            // The medic always follows + heals, grunts take the orders.
            var minionOrder = comp.Medic
                ? NpcOrder.Follow
                : c.Order switch
                {
                    NpcOrder.Attack => NpcOrder.Attack,
                    NpcOrder.Retreat => NpcOrder.Retreat,
                    // Builders get Hold so they build somewhat near the commander.
                    NpcOrder.Hold when HasComp<NpcBuilderComponent>(minion) => NpcOrder.Hold,
                    _ => NpcOrder.Follow,
                };

            SetCurrentOrder(minion, minionOrder, minionOrder == NpcOrder.Attack ? c.AssaultTarget : null, ent.Owner);

            // Relay the assault target's last-known position so grunts can push to it.
            if (minionOrder == NpcOrder.Attack && c.AssaultTargetCoordinates is { } atc && TryComp<HTNComponent>(minion, out var minionHtn))
                minionHtn.Blackboard.SetValue(OrderedTargetCoordinatesKey, atc);
        }

        // Kicks slowpoks from squad
        foreach (var old in c.Retinue)
        {
            if (chosen.Contains(old))
                continue;

            if (TryComp<NpcMinionComponent>(old, out var comp) && comp.Commander == ent.Owner)
            {
                comp.Commander = null;
                ClearOrder(old);
            }
        }

        c.Retinue = chosen;
    }

    // The squad order state
    private NpcOrder DecideOrder(NpcCommanderComponent c, TimeSpan now, EntityUid? enemy, float hp, bool reduced)
    {
        // Flee NOW.
        if (hp <= c.RetreatHealth)
        {
            c.OrderUntil = now + c.RetreatDuration;
            c.EngageSince = null;
            return NpcOrder.Retreat;
        }

        if (c.Order == NpcOrder.Attack && c.OrderUntil is { } au && now < au)
            return NpcOrder.Attack;

        if (c.Order == NpcOrder.Retreat && c.OrderUntil is { } ru && now < ru)
            return NpcOrder.Retreat;

        // Hold - timed, but renewed if got down members, exited once the squad is whole again.
        if (c.Order == NpcOrder.Hold)
        {
            c.EngageSince = null;
            if (!reduced)
                return NpcOrder.Follow;
            if (c.OrderUntil is { } hu && now < hu)
                return NpcOrder.Hold;
            return EnterHold(c, now);
        }

        // What to do after assault ended.
        if (c.Order == NpcOrder.Attack)
        {
            c.EngageSince = null;
            if (reduced)
                return EnterHold(c, now);
            if (enemy == null)
            {
                c.OrderUntil = now + c.RetreatDuration;
                return NpcOrder.Retreat;
            }
            return NpcOrder.Follow;
        }

        // Patrol / engage.
        if (reduced)
            return EnterHold(c, now);

        if (enemy != null)
        {
            c.LastSeenEnemyTime = now;
            c.EngageSince ??= now;
            if (now - c.EngageSince.Value >= c.EngageDelay && now >= c.NextAssault)
            {
                c.AssaultTarget = enemy;
                c.AssaultTargetCoordinates = null;
                c.OrderUntil = now + c.AssaultDuration;
                c.NextAssault = now + c.AssaultDuration + c.AssaultCooldown;
                return NpcOrder.Attack;
            }

            return NpcOrder.Follow;
        }

        // If enemy lost - keep the engage timer alive for grace preiood so it doesn't restart the engage delay.
        if (c.LastSeenEnemyTime is { } seen && now - seen < c.EnemyGrace)
            return NpcOrder.Follow;

        c.EngageSince = null;
        return NpcOrder.Follow;
    }

    private IEnumerable<EntityUid> PickSlots(List<(EntityUid Ent, float Dist)> candidates, HashSet<EntityUid> current, int max)
    {
        return candidates
            .OrderBy(x => current.Contains(x.Ent) ? 0 : 1)
            .ThenBy(x => x.Dist)
            .Take(max)
            .Select(x => x.Ent);
    }

    private EntityUid? GetEnemy(Entity<NpcCommanderComponent> ent)
    {
        if (!TryComp<HTNComponent>(ent.Owner, out var htn))
            return null;

        if (!htn.Blackboard.TryGetValue<EntityUid>(ent.Comp.TargetKey, out var enemy, EntityManager))
            return null;

        return Deleted(enemy) || _mobState.IsDead(enemy) ? null : enemy;
    }

    private NpcOrder EnterHold(NpcCommanderComponent c, TimeSpan now)
    {
        c.OrderUntil = now + c.HoldDuration;
        c.EngageSince = null;
        return NpcOrder.Hold;
    }

    private float HealthFraction(EntityUid uid)
    {
        if (!TryComp<DamageableComponent>(uid, out var dmg))
            return 1f;

        if (!_thresholds.TryGetIncapPercentage(uid, dmg.TotalDamage, out var incap))
            return 1f;

        return (float) (1 - incap);
    }

    private void Announce(EntityUid commander, NpcCommanderComponent comp, NpcOrder order)
    {
        // Non-speaking commanders like fauna play a sound instead of voiceline.
        if (comp.OrderSound != null)
        {
            _audio.PlayPvs(comp.OrderSound, commander);
            return;
        }

        var pack = order switch
        {
            NpcOrder.Attack => comp.AttackLines,
            NpcOrder.Hold => comp.HoldLines,
            NpcOrder.Retreat => comp.RetreatLines,
            _ => default,
        };

        if (string.IsNullOrEmpty(pack.Id) || !_proto.TryIndex(pack, out var lines) || lines.Values.Count == 0)
            return;

        _chat.TrySendInGameICMessage(commander, Loc.GetString(_random.Pick(lines.Values)), InGameICChatType.Speak, hideChat: false, hideLog: true);
    }

    // Sets CurrentOrders + the relevant target/follow key and replans if change.
    private void SetCurrentOrder(EntityUid uid, NpcOrder order, EntityUid? target, EntityUid commander)
    {
        if (!TryComp<HTNComponent>(uid, out var htn))
            return;

        var bb = htn.Blackboard;
        var changed = !Equals(bb.GetValueOrDefault<Enum>(NPCBlackboard.CurrentOrders, EntityManager), order);

        if (order == NpcOrder.Attack)
        {
            // So grunts dont target a missing/deleted entity.
            if (target is not { } attackTarget || Deleted(attackTarget))
                return;

            var cur = bb.GetValueOrDefault<EntityUid>(NPCBlackboard.CurrentOrderedTarget, EntityManager);
            if (!changed && cur == attackTarget)
                return;

            bb.SetValue(NPCBlackboard.CurrentOrders, order);
            bb.SetValue(NPCBlackboard.CurrentOrderedTarget, attackTarget);
        }
        else
        {
            if (!changed)
                return;

            bb.SetValue(NPCBlackboard.CurrentOrders, order);
            bb.SetValue(NPCBlackboard.FollowTarget, new EntityCoordinates(commander, Vector2.Zero));
        }

        Replan(htn);
    }

    private void ClearOrder(EntityUid minion)
    {
        if (!TryComp<HTNComponent>(minion, out var htn))
            return;

        var bb = htn.Blackboard;
        bb.Remove<Enum>(NPCBlackboard.CurrentOrders);
        bb.Remove<EntityUid>(NPCBlackboard.CurrentOrderedTarget);
        bb.Remove<EntityCoordinates>(OrderedTargetCoordinatesKey);
        Replan(htn);
    }

    private void Replan(HTNComponent htn)
    {
        if (htn.Plan != null)
            _htn.ShutdownPlan(htn);

        _htn.Replan(htn);
    }
    
    // clear
    private void OnCommanderShutdown(Entity<NpcCommanderComponent> ent, ref ComponentShutdown args)
    {
        DisbandSquad(ent);
    }

    // On commander death releases every member and resets squad state.
    private void DisbandSquad(Entity<NpcCommanderComponent> ent)
    {
        foreach (var minion in ent.Comp.Retinue)
        {
            if (TryComp<NpcMinionComponent>(minion, out var comp) && comp.Commander == ent.Owner)
            {
                comp.Commander = null;
                ClearOrder(minion);
            }
        }

        ent.Comp.Retinue.Clear();
        ent.Comp.Members.Clear();
        ent.Comp.Order = NpcOrder.Follow;
    }
}
