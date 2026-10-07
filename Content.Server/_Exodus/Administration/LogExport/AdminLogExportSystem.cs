using Content.Shared._Exodus.Administration.LogExport;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Administration.LogExport;

/// <summary>Server-wide admission and lifetime bounds for round-log exports, independent of EUI windows.</summary>
public sealed partial class AdminLogExportSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    private readonly Dictionary<NetUserId, Lease> _active = new();
    private readonly Dictionary<NetUserId, TimeSpan> _cooldowns = new();
    private readonly List<NetUserId> _expiredCooldowns = new();
    private readonly List<Lease> _expiredLeases = new(AdminLogExportLimits.ConcurrentExports);
    private TimeSpan _nextCheck;

    public int ActiveCount => _active.Count;

    public override void Initialize()
    {
        base.Initialize();
        _nextCheck = _timing.RealTime + TimeSpan.FromSeconds(1);
    }

    public sealed class Lease(NetUserId user, Guid id, TimeSpan now, Action<string> cancel)
    {
        public NetUserId User { get; } = user;
        public Guid Id { get; } = id;
        internal TimeSpan Started { get; } = now;
        internal TimeSpan LastActivity = now;
        internal bool CancellationRequested;
        internal Action<string> Cancel { get; } = cancel;
    }

    public bool TryAcquire(NetUserId user, Guid id, TimeSpan now, Action<string> cancel,
        out Lease lease, out string error)
    {
        lease = default!;
        if (_active.ContainsKey(user) || _active.Count >= AdminLogExportLimits.ConcurrentExports)
        {
            error = "admin-logs-export-error-busy";
            return false;
        }

        if (_cooldowns.TryGetValue(user, out var until) && now < until)
        {
            error = "admin-logs-export-error-cooldown";
            return false;
        }

        lease = new Lease(user, id, now, cancel);
        _active.Add(user, lease);
        _cooldowns[user] = now + AdminLogExportLimits.UserCooldown;
        error = string.Empty;
        return true;
    }

    public void Touch(Lease lease, TimeSpan now)
    {
        if (!lease.CancellationRequested)
            lease.LastActivity = now;
    }

    public void Release(Lease lease)
    {
        if (_active.TryGetValue(lease.User, out var current) && ReferenceEquals(current, lease))
            _active.Remove(lease.User);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_timing.RealTime < _nextCheck)
            return;

        _nextCheck += TimeSpan.FromSeconds(1);
        Expire(_timing.RealTime);
    }

    public void Expire(TimeSpan now)
    {
        // At most two active leases. Collect first because callbacks may release their leases.
        foreach (var lease in _active.Values)
        {
            if (lease.CancellationRequested ||
                now - lease.Started < AdminLogExportLimits.ExportLifetime &&
                now - lease.LastActivity < AdminLogExportLimits.IdleTimeout)
                continue;

            lease.CancellationRequested = true;
            _expiredLeases.Add(lease);
        }

        foreach (var lease in _expiredLeases)
            lease.Cancel("admin-logs-export-error-timeout");
        _expiredLeases.Clear();

        foreach (var (user, until) in _cooldowns)
        {
            if (now >= until && !_active.ContainsKey(user))
                _expiredCooldowns.Add(user);
        }

        foreach (var user in _expiredCooldowns)
            _cooldowns.Remove(user);
        _expiredCooldowns.Clear();
    }

    public override void Shutdown()
    {
        foreach (var lease in _active.Values)
            _expiredLeases.Add(lease);
        foreach (var lease in _expiredLeases)
            lease.Cancel("admin-logs-export-error-cancelled");
        _expiredLeases.Clear();
        base.Shutdown();
    }
}
