using Content.Shared._Exodus.Stealth.Components;
using Robust.Shared.Network;

namespace Content.Shared._Exodus.Stealth.Systems;

public abstract partial class SharedStealthSystem
{
    [Dependency] private INetManager _net = default!;

    private EntityQuery<StealthSuppressedComponent> _suppressedQuery;

    private void InitializeSuppression()
    {
        _suppressedQuery = GetEntityQuery<StealthSuppressedComponent>();
        SubscribeLocalEvent<StealthSuppressedComponent, ComponentStartup>(OnSuppressionChanged);
        SubscribeLocalEvent<StealthSuppressedComponent, AfterAutoHandleStateEvent>(OnSuppressionChanged);
        SubscribeLocalEvent<StealthSuppressedComponent, ComponentShutdown>(OnSuppressionChanged);
    }

    public bool IsSuppressed(EntityUid uid)
    {
        return _suppressedQuery.TryComp(uid, out var suppressed) &&
               suppressed.LifeStage < ComponentLifeStage.Stopping;
    }

    /// <summary>Reveals a cloaked or disguised target and temporarily suppresses further concealment.</summary>
    public bool TrySuppress(Entity<StealthComponent?> ent, TimeSpan duration)
    {
        if (_net.IsClient || duration <= TimeSpan.Zero || TerminatingOrDeleted(ent))
            return false;

        var attempt = new StealthRevealAttemptEvent(Resolve(ent.Owner, ref ent.Comp, false) && !IsVisible(ent));
        RaiseLocalEvent(ent, ref attempt);
        if (!attempt.CanReveal)
            return false;

        var suppressed = EnsureComp<StealthSuppressedComponent>(ent);
        suppressed.Until = _timing.CurTime + duration;
        Dirty(ent, suppressed);

        // Active abilities must update their own enabled state and cooldowns. Passive layers stay registered.
        var reveal = new StealthRevealEvent(ent.Owner);
        RaiseLocalEvent(ent, reveal);
        return true;
    }

    private void OnSuppressionChanged<T>(Entity<StealthSuppressedComponent> ent, ref T args)
    {
        if (TerminatingOrDeleted(ent))
            return;

        var changed = new StealthRequestChangeEvent();
        RaiseLocalEvent(ent, changed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<StealthSuppressedComponent>();
        while (query.MoveNext(out var uid, out var suppressed))
        {
            if (suppressed.Until > _timing.CurTime || TerminatingOrDeleted(uid))
                continue;

            // Passive sources fade back in from visible, without accumulating invisibility during suppression.
            if (TryComp<StealthComponent>(uid, out var stealth))
            {
                foreach (var data in stealth.StealthLayers.Values)
                    data.LastVisibility = Math.Clamp(1f, data.MinVisibility, data.MaxVisibility);
                stealth.LastUpdated = _timing.CurTime;
                Dirty(uid, stealth);
            }
            RemCompDeferred<StealthSuppressedComponent>(uid);
        }
    }
}
