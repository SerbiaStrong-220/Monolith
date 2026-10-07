using System.Numerics;
using Content.Server._Exodus.Mining.Pipes;
using Content.Server._Exodus.Mining.Pipes.Components;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Mining.AutoMining;
using Content.Shared.Database;
using Content.Shared.Popups;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>
/// Mining consortiums: two ships aim a free bulk mining laser at each other, joining their liquid metal networks.
/// Links are pairwise; every connected set of linked ships shares slurry, mining speed and refinery yield bonuses.
/// </summary>
public sealed partial class BulkAutoMiningSystem
{
    [Dependency] private MiningPipeNetSystem _pipes = default!;


    /// <summary>Link raycasts per tick; links are few and each is checked twice per second.</summary>
    private const int MaxLinkChecksPerTick = 16;

    private static readonly TimeSpan RequestSweepInterval = TimeSpan.FromSeconds(1);

    private EntityQuery<BulkMiningLinkGridComponent> _linkGridQuery;
    private EntityQuery<BulkAutoMiningConsoleComponent> _consoleQuery;

    private float _linkBonus;
    private float _linkSpeedBonus;
    private float _linkBonusDecay;
    private float _linkMaxBonus;
    private TimeSpan _nextRequestSweep;

    private readonly List<EntityUid> _ownLasers = new();
    private readonly List<EntityUid> _otherLasers = new();
    private readonly List<(EntityUid A, EntityUid B, float Distance)> _laserPairs = new();
    private readonly List<(EntityUid Grid, float Distance)> _linkCandidates = new();
    private readonly List<(EntityUid Grid, EntityUid Partner)> _linkPairs = new();
    private readonly List<EntityUid> _consortium = new();
    private readonly List<EntityUid> _linkConsoles = new();
    private readonly List<(EntityUid Emitter, BulkMiningLinkBreakReason Reason)> _linkBreaks = new();
    private readonly List<EntityUid> _expiredRequests = new();

    private void InitializeLinks()
    {
        _linkGridQuery = GetEntityQuery<BulkMiningLinkGridComponent>();
        _consoleQuery = GetEntityQuery<BulkAutoMiningConsoleComponent>();
        Subs.CVar(_cfg, EXCVars.BulkMiningLinkBonus, value => _linkBonus = value, true);
        Subs.CVar(_cfg, EXCVars.BulkMiningLinkSpeedBonus, value => _linkSpeedBonus = value, true);
        Subs.CVar(_cfg, EXCVars.BulkMiningLinkBonusDecay, value => _linkBonusDecay = value, true);
        Subs.CVar(_cfg, EXCVars.BulkMiningLinkMaxBonus, value => _linkMaxBonus = value, true);
    }

    #region Messages

    private void OnLinkRequest(Entity<BulkAutoMiningConsoleComponent> ent, ref BulkAutoMiningLinkRequestMessage args)
    {
        if (TryGetEntity(args.Grid, out var target))
            TryRequestLink(ent, target.Value, args.Actor);
    }

    private void OnLinkAccept(Entity<BulkAutoMiningConsoleComponent> ent, ref BulkAutoMiningLinkAcceptMessage args)
    {
        if (TryGetEntity(args.Grid, out var requester))
            TryAcceptLink(ent, requester.Value, args.Actor);
    }

    private void OnLinkDecline(Entity<BulkAutoMiningConsoleComponent> ent, ref BulkAutoMiningLinkDeclineMessage args)
    {
        if (TryGetEntity(args.Grid, out var target))
            DeclineLink(ent, target.Value);
    }

    private void OnLinkBreak(Entity<BulkAutoMiningConsoleComponent> ent, ref BulkAutoMiningLinkBreakMessage args)
    {
        if (TryGetEntity(args.Grid, out var target))
            TryBreakLink(ent, target.Value, args.Actor);
    }

    /// <summary>
    /// Asks another ship to link. A request crossing the other ship's own request links them at once.
    /// Returns true when a request was sent or a link was made.
    /// </summary>
    public bool TryRequestLink(Entity<BulkAutoMiningConsoleComponent> ent, EntityUid target, EntityUid? actor = null)
    {
        if (!TryGetLinkGrids(ent, target, out var ownGrid))
            return false;

        var now = _timing.CurTime;
        var own = EnsureComp<BulkMiningLinkGridComponent>(ownGrid);
        if (AreLinked(ownGrid, target))
        {
            LinkPopup(ent, "bulk-auto-mining-link-popup-already-linked", target);
            return false;
        }

        // Crossing requests are consent from both ships.
        if (own.Incoming.ContainsKey(target))
            return AcceptLink(ent, ownGrid, target, own, actor);

        string? failure = null;
        if (own.Outgoing.ContainsKey(target))
            failure = "bulk-auto-mining-link-popup-already-requested";
        else if (now < own.NextRequestTime)
            failure = "bulk-auto-mining-link-popup-cooldown";
        else if (own.Outgoing.Count >= ent.Comp.MaxOutgoingLinkRequests)
            failure = "bulk-auto-mining-link-popup-too-many";
        else if (!IsGridInRange(ent, target, ent.Comp.MaxRange))
            failure = "bulk-auto-mining-link-popup-out-of-range";
        else if (!TryGetLinkConsole(target, out _))
            failure = "bulk-auto-mining-link-popup-no-console";
        else if (CountFreeLasers(ownGrid) == 0)
            failure = "bulk-auto-mining-link-popup-no-free-laser";

        if (failure != null)
        {
            LinkPopup(ent, failure, target);
            return false;
        }

        var other = EnsureComp<BulkMiningLinkGridComponent>(target);
        var expires = now + ent.Comp.LinkRequestTimeout;
        own.Outgoing[target] = expires;
        other.Incoming[ownGrid] = expires;
        own.NextRequestTime = now + ent.Comp.LinkRequestCooldown;
        PopupLinkConsoles(target, "bulk-auto-mining-link-popup-incoming", ownGrid, PopupType.Medium);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(actor):player} requested a bulk mining link from {ToPrettyString(ownGrid)} to {ToPrettyString(target)}.");
        UpdateLinkUis(ownGrid, target);
        return true;
    }

    /// <summary>Accepts a pending request of another ship, linking the nearest free lasers that can see each other.</summary>
    public bool TryAcceptLink(Entity<BulkAutoMiningConsoleComponent> ent, EntityUid requester, EntityUid? actor = null)
    {
        if (!TryGetLinkGrids(ent, requester, out var ownGrid))
            return false;

        var own = EnsureComp<BulkMiningLinkGridComponent>(ownGrid);
        if (!own.Incoming.TryGetValue(requester, out var expires) || expires < _timing.CurTime)
        {
            LinkPopup(ent, "bulk-auto-mining-link-popup-request-expired", requester);
            UpdateLinkUis(ownGrid, requester);
            return false;
        }

        return AcceptLink(ent, ownGrid, requester, own, actor);
    }

    /// <summary>Declines a request of another ship or withdraws this ship's own request to it.</summary>
    public bool DeclineLink(Entity<BulkAutoMiningConsoleComponent> ent, EntityUid target)
    {
        if (Transform(ent).GridUid is not { } ownGrid || !_linkGridQuery.TryComp(ownGrid, out var own))
            return false;

        _linkGridQuery.TryComp(target, out var other);
        var removed = true;
        if (own.Incoming.Remove(target))
        {
            other?.Outgoing.Remove(ownGrid);
            PopupLinkConsoles(target, "bulk-auto-mining-link-popup-declined", ownGrid, PopupType.MediumCaution);
        }
        else if (own.Outgoing.Remove(target))
        {
            other?.Incoming.Remove(ownGrid);
        }
        else
        {
            removed = false;
        }

        UpdateLinkUis(ownGrid, target);
        return removed;
    }

    /// <summary>Breaks this ship's link with another ship. Either side may do so at any time.</summary>
    public bool TryBreakLink(Entity<BulkAutoMiningConsoleComponent> ent, EntityUid target, EntityUid? actor = null)
    {
        if (Transform(ent).GridUid is not { } ownGrid)
            return false;

        var query = EntityQueryEnumerator<BulkAutoMiningEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var emitter, out var xform))
        {
            if (xform.GridUid == ownGrid && emitter.LinkGrid == target && emitter.LinkPartner != null)
                _linkBreaks.Add((uid, BulkMiningLinkBreakReason.Manual));
        }

        if (_linkBreaks.Count == 0)
            return false;

        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(actor):player} broke the bulk mining link between {ToPrettyString(ownGrid)} and {ToPrettyString(target)}.");
        ApplyLinkBreaks();
        return true;
    }

    /// <summary>The console always acts for its own ship; the other ship must be a real, different grid.</summary>
    private bool TryGetLinkGrids(Entity<BulkAutoMiningConsoleComponent> console, EntityUid target, out EntityUid ownGrid)
    {
        ownGrid = default;
        if (!IsPoweredAndAnchored(console) || Transform(console).GridUid is not { } grid)
        {
            Popup(console, "bulk-auto-mining-stopped-power");
            return false;
        }

        if (target == grid || TerminatingOrDeleted(target) || !_gridQuery.HasComp(target))
            return false;

        ownGrid = grid;
        return true;
    }

    private bool AcceptLink(Entity<BulkAutoMiningConsoleComponent> console, EntityUid ownGrid, EntityUid requester,
        BulkMiningLinkGridComponent own, EntityUid? actor)
    {
        own.Incoming.Remove(requester);
        own.Outgoing.Remove(requester);
        var other = EnsureComp<BulkMiningLinkGridComponent>(requester);
        other.Incoming.Remove(ownGrid);
        other.Outgoing.Remove(ownGrid);

        var failure = TryCreateLink(console, ownGrid, requester);
        if (failure != null)
        {
            LinkPopup(console, failure, requester);
            PopupLinkConsoles(requester, failure, ownGrid, PopupType.MediumCaution);
        }
        else
        {
            PopupLinkConsoles(ownGrid, "bulk-auto-mining-link-popup-established", requester, PopupType.Medium);
            PopupLinkConsoles(requester, "bulk-auto-mining-link-popup-established", ownGrid, PopupType.Medium);
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(actor):player} linked the liquid metal networks of {ToPrettyString(ownGrid)} and {ToPrettyString(requester)}.");
        }

        UpdateLinkUis(ownGrid, requester);
        return failure == null;
    }

    #endregion

    #region Links

    /// <summary>Links the nearest pair of free lasers with a clear line of sight. Returns a failure message on error.</summary>
    private string? TryCreateLink(Entity<BulkAutoMiningConsoleComponent> console, EntityUid ownGrid, EntityUid otherGrid)
    {
        if (AreLinked(ownGrid, otherGrid))
            return "bulk-auto-mining-link-popup-already-linked";

        if (!IsGridInRange(console, otherGrid, console.Comp.MaxRange))
            return "bulk-auto-mining-link-popup-out-of-range";

        if (!TryGetLinkConsole(otherGrid, out var otherConsole))
            return "bulk-auto-mining-link-popup-no-console";

        var range = Math.Min(console.Comp.MaxRange, otherConsole.Comp.MaxRange);
        if (!TryFindLaserPair(ownGrid, otherGrid, range, out var ownLaser, out var otherLaser, out var failure))
            return failure;

        SetLink(ownLaser, otherLaser, range);
        return null;
    }

    /// <summary>The nearest pair of free lasers in range whose beam is not obstructed.</summary>
    private bool TryFindLaserPair(EntityUid ownGrid, EntityUid otherGrid, float range,
        out EntityUid ownLaser, out EntityUid otherLaser, out string failure)
    {
        ownLaser = otherLaser = default;
        _ownLasers.Clear();
        _otherLasers.Clear();
        GetFreeLasers(ownGrid, _ownLasers);
        GetFreeLasers(otherGrid, _otherLasers);
        if (_ownLasers.Count == 0 || _otherLasers.Count == 0)
        {
            failure = "bulk-auto-mining-link-popup-no-free-laser";
            return false;
        }

        _laserPairs.Clear();
        foreach (var own in _ownLasers)
        {
            var ownPosition = _transform.GetWorldPosition(own);
            foreach (var other in _otherLasers)
            {
                var distance = Vector2.Distance(ownPosition, _transform.GetWorldPosition(other));
                if (distance <= range)
                    _laserPairs.Add((own, other, distance));
            }
        }

        if (_laserPairs.Count == 0)
        {
            failure = "bulk-auto-mining-link-popup-out-of-range";
            return false;
        }

        _laserPairs.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        foreach (var (own, other, _) in _laserPairs)
        {
            if (!IsLinkClear(own, other))
                continue;

            ownLaser = own;
            otherLaser = other;
            failure = string.Empty;
            return true;
        }

        failure = "bulk-auto-mining-link-popup-obstructed";
        return false;
    }

    private void SetLink(EntityUid first, EntityUid second, float range)
    {
        SetLinkEnd(first, second, range);
        SetLinkEnd(second, first, range);
        _pipes.InvalidateJoinedNetworks(first);
        RefreshConsortium(Transform(first).GridUid);
    }

    private void SetLinkEnd(EntityUid uid, EntityUid partner, float range)
    {
        var comp = _emitterQuery.GetComponent(uid);
        var partnerXform = Transform(partner);
        ClearBeam((uid, comp));
        comp.LinkPartner = partner;
        comp.LinkGrid = partnerXform.GridUid;
        comp.LinkPosition = partnerXform.LocalPosition;
        comp.LinkRange = range;
        comp.LinkObstructedChecks = 0;
        comp.NextLinkCheck = _timing.CurTime + comp.LinkCheckInterval;
        comp.StartupStream = _audio.PlayPvs(comp.StartSound, uid)?.Entity;
        _ambient.SetAmbience(uid, true);
        EnsureComp<MiningPipeBridgeComponent>(uid).Partner = partner;
        Dirty(uid, comp);
    }

    /// <summary>Breaks the link of a laser on both ends and reports it to both ships.</summary>
    private void BreakLink(Entity<BulkAutoMiningEmitterComponent> ent, BulkMiningLinkBreakReason reason)
    {
        if (ent.Comp.LinkPartner is not { } partner)
            return;

        var otherGrid = ent.Comp.LinkGrid;
        // The partner remembers which ship the link was made with, even after this laser left it.
        var ownGrid = _xformQuery.TryComp(ent, out var xform) ? xform.GridUid : null;
        ClearLink(ent);
        ent.Comp.LastLinkBreak = reason;
        if (_emitterQuery.TryComp(partner, out var partnerComp) && partnerComp.LinkPartner == ent.Owner)
        {
            ownGrid = partnerComp.LinkGrid ?? ownGrid;
            ClearLink((partner, partnerComp));
            partnerComp.LastLinkBreak = reason;
        }

        if (!TerminatingOrDeleted(ent))
            _pipes.InvalidateJoinedNetworks(ent);

        if (!TerminatingOrDeleted(partner))
            _pipes.InvalidateJoinedNetworks(partner);

        RefreshConsortium(ownGrid);
        RefreshConsortium(otherGrid);

        if (ownGrid is not { } a || otherGrid is not { } b || TerminatingOrDeleted(a) || TerminatingOrDeleted(b))
            return;

        var popup = reason switch
        {
            BulkMiningLinkBreakReason.Obstructed => "bulk-auto-mining-link-popup-broken-obstructed",
            BulkMiningLinkBreakReason.Range => "bulk-auto-mining-link-popup-broken-range",
            BulkMiningLinkBreakReason.Power => "bulk-auto-mining-link-popup-broken-power",
            BulkMiningLinkBreakReason.Manual => "bulk-auto-mining-link-popup-broken-manual",
            _ => "bulk-auto-mining-link-popup-broken-lost",
        };
        PopupLinkConsoles(a, popup, b, PopupType.MediumCaution);
        PopupLinkConsoles(b, popup, a, PopupType.MediumCaution);
        UpdateLinkUis(a, b);
    }

    private void ClearLink(Entity<BulkAutoMiningEmitterComponent> ent)
    {
        ent.Comp.LinkPartner = null;
        ent.Comp.LinkGrid = null;
        ent.Comp.LinkObstructedChecks = 0;
        if (TerminatingOrDeleted(ent))
            return;

        RemComp<MiningPipeBridgeComponent>(ent);
        if (ent.Comp.BeamGrid == null)
            StopEmitterAudio(ent);

        Dirty(ent);
    }

    private void ApplyLinkBreaks()
    {
        foreach (var (uid, reason) in _linkBreaks)
        {
            if (_emitterQuery.TryComp(uid, out var comp))
                BreakLink((uid, comp), reason);
        }

        _linkBreaks.Clear();
    }

    private void UpdateLinks(TimeSpan now)
    {
        if (now >= _nextRequestSweep)
        {
            _nextRequestSweep = now + RequestSweepInterval;
            SweepRequests(now);
        }

        var checks = MaxLinkChecksPerTick;
        var query = EntityQueryEnumerator<BulkAutoMiningEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (comp.LinkPartner is not { } partner || now < comp.NextLinkCheck)
                continue;

            if (!_emitterQuery.TryComp(partner, out var partnerComp) || partnerComp.LinkPartner != uid)
            {
                _linkBreaks.Add((uid, BulkMiningLinkBreakReason.Lost));
                continue;
            }

            // One end checks each link.
            if (uid.CompareTo(partner) > 0)
                continue;

            if (checks-- <= 0)
                break;

            // A late check has nothing to catch up on, so schedule from now instead of accumulating.
            comp.NextLinkCheck = now + comp.LinkCheckInterval;
            if (GetLinkFailure((uid, comp, xform), (partner, partnerComp)) is { } failure)
                _linkBreaks.Add((uid, failure));
        }

        ApplyLinkBreaks();
    }

    private BulkMiningLinkBreakReason? GetLinkFailure(Entity<BulkAutoMiningEmitterComponent, TransformComponent> ent,
        Entity<BulkAutoMiningEmitterComponent> partner)
    {
        if (!_powerQuery.TryComp(ent, out var power) || !power.Powered ||
            !_powerQuery.TryComp(partner, out var partnerPower) || !partnerPower.Powered)
            return BulkMiningLinkBreakReason.Power;

        var partnerXform = _xformQuery.GetComponent(partner);
        if (!IsPoweredAndAnchored(ent) || !IsPoweredAndAnchored(partner) ||
            ent.Comp2.GridUid != partner.Comp.LinkGrid || partnerXform.GridUid != ent.Comp1.LinkGrid ||
            ent.Comp2.MapUid == null || ent.Comp2.MapUid != partnerXform.MapUid)
            return BulkMiningLinkBreakReason.Lost;

        var distance = Vector2.Distance(_transform.GetWorldPosition(ent.Comp2), _transform.GetWorldPosition(partnerXform));
        if (distance > ent.Comp1.LinkRange)
            return BulkMiningLinkBreakReason.Range;

        if (IsLinkClear(ent, partner))
        {
            ent.Comp1.LinkObstructedChecks = 0;
            partner.Comp.LinkObstructedChecks = 0;
            return null;
        }

        partner.Comp.LinkObstructedChecks = ++ent.Comp1.LinkObstructedChecks;
        return ent.Comp1.LinkObstructedChecks > ent.Comp1.LinkObstructionTolerance
            ? BulkMiningLinkBreakReason.Obstructed
            : null;
    }

    /// <summary>Anything but the two lasers and the two ships' own shields blocks a link.</summary>
    private bool IsLinkClear(EntityUid first, EntityUid second)
    {
        var firstXform = _xformQuery.GetComponent(first);
        var secondXform = _xformQuery.GetComponent(second);
        if (firstXform.MapUid == null || firstXform.MapUid != secondXform.MapUid)
            return false;

        var filter = new BeamFilter(this, first, firstXform.GridUid, second, secondXform.GridUid, null);
        return IsRayClear(firstXform.MapID, _transform.GetWorldPosition(firstXform),
            _transform.GetWorldPosition(secondXform), filter);
    }

    private void SweepRequests(TimeSpan now)
    {
        var query = EntityQueryEnumerator<BulkMiningLinkGridComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            _expiredRequests.Clear();
            foreach (var (target, expires) in comp.Outgoing)
            {
                if (expires <= now || TerminatingOrDeleted(target))
                    _expiredRequests.Add(target);
            }

            foreach (var target in _expiredRequests)
            {
                comp.Outgoing.Remove(target);
                if (!TerminatingOrDeleted(target) && _linkGridQuery.TryComp(target, out var other))
                    other.Incoming.Remove(uid);

                UpdateLinkUis(uid, target);
            }

            // Requests from ships that vanished are dropped silently.
            _expiredRequests.Clear();
            foreach (var requester in comp.Incoming.Keys)
            {
                if (TerminatingOrDeleted(requester))
                    _expiredRequests.Add(requester);
            }

            foreach (var requester in _expiredRequests)
            {
                comp.Incoming.Remove(requester);
            }
        }
    }

    #endregion

    #region Queries

    private bool IsFreeForLink(EntityUid uid)
    {
        return _emitterQuery.TryComp(uid, out var comp) && comp.LinkPartner == null && comp.BeamGrid == null &&
               IsPoweredAndAnchored(uid) &&
               (comp.Controller is not { } controller || TerminatingOrDeleted(controller) ||
                !_consoleQuery.TryComp(controller, out var console) || !console.Active);
    }

    private void GetFreeLasers(EntityUid grid, List<EntityUid> lasers)
    {
        GetGridEmitters(grid, lasers);
        for (var i = lasers.Count - 1; i >= 0; i--)
        {
            if (!IsFreeForLink(lasers[i]))
                lasers.RemoveAt(i);
        }
    }

    private int CountFreeLasers(EntityUid grid)
    {
        var count = 0;
        var query = EntityQueryEnumerator<BulkAutoMiningEmitterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid && IsFreeForLink(uid))
                count++;
        }

        return count;
    }

    private bool AreLinked(EntityUid grid, EntityUid other)
    {
        var query = EntityQueryEnumerator<BulkAutoMiningEmitterComponent, TransformComponent>();
        while (query.MoveNext(out _, out var comp, out var xform))
        {
            if (xform.GridUid == grid && comp.LinkPartner != null && comp.LinkGrid == other)
                return true;
        }

        return false;
    }

    /// <summary>The first powered, anchored bulk mining console of a ship; it answers link requests.</summary>
    private bool TryGetLinkConsole(EntityUid grid, out Entity<BulkAutoMiningConsoleComponent> console)
    {
        var query = EntityQueryEnumerator<BulkAutoMiningConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var comp, out var xform))
        {
            if (xform.GridUid != grid || !IsPoweredAndAnchored(uid))
                continue;

            console = (uid, comp);
            return true;
        }

        console = default;
        return false;
    }

    /// <summary>Ships connected to this one through links, including itself.</summary>
    private void CollectConsortium(EntityUid grid, List<EntityUid> members)
    {
        members.Clear();
        _linkPairs.Clear();
        var query = EntityQueryEnumerator<BulkAutoMiningEmitterComponent, TransformComponent>();
        while (query.MoveNext(out _, out var comp, out var xform))
        {
            if (comp.LinkPartner != null && comp.LinkGrid is { } other && xform.GridUid is { } own)
                _linkPairs.Add((own, other));
        }

        members.Add(grid);
        for (var i = 0; i < members.Count; i++)
        {
            foreach (var (own, other) in _linkPairs)
            {
                if (own == members[i] && !members.Contains(other))
                    members.Add(other);
            }
        }
    }

    /// <summary>Number of ships linked with this one, directly or through other members, including itself.</summary>
    public int GetConsortiumSize(EntityUid grid)
    {
        return _linkGridQuery.TryComp(grid, out var links) ? links.ConsortiumSize : 1;
    }

    /// <summary>Refreshes the component cache on topology changes, avoiding graph searches during mining.</summary>
    private void RefreshConsortium(EntityUid? grid)
    {
        if (grid is not { } uid || TerminatingOrDeleted(uid))
            return;

        CollectConsortium(uid, _consortium);
        foreach (var member in _consortium)
        {
            if (!TerminatingOrDeleted(member))
                EnsureComp<BulkMiningLinkGridComponent>(member).ConsortiumSize = _consortium.Count;
        }
    }

    /// <summary>Refinery yield bonus of a consortium of this size.</summary>
    public float GetConsortiumBonus(int ships)
    {
        return BulkMiningLinkBonus.Get(ships, _linkBonus, _linkBonusDecay, _linkMaxBonus);
    }

    /// <summary>Mining speed bonus of a consortium of this size.</summary>
    public float GetConsortiumSpeedBonus(int ships)
    {
        return BulkMiningLinkBonus.Get(ships, _linkSpeedBonus, _linkBonusDecay, _linkMaxBonus);
    }

    private string GetShipName(EntityUid grid)
    {
        return TerminatingOrDeleted(grid) ? Loc.GetString("bulk-auto-mining-unknown-grid") : Name(grid);
    }

    #endregion

    #region Notifications

    private void LinkPopup(EntityUid console, string locId, EntityUid other)
    {
        _popup.PopupEntity(Loc.GetString(locId, ("ship", GetShipName(other))), console, PopupType.SmallCaution);
    }

    private void PopupLinkConsoles(EntityUid grid, string locId, EntityUid other, PopupType type)
    {
        var message = Loc.GetString(locId, ("ship", GetShipName(other)));
        var query = EntityQueryEnumerator<BulkAutoMiningConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == grid)
                _popup.PopupEntity(message, uid, type);
        }
    }

    private void UpdateLinkUis(EntityUid first, EntityUid second)
    {
        _linkConsoles.Clear();
        var query = EntityQueryEnumerator<BulkAutoMiningConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid == first || xform.GridUid == second)
                _linkConsoles.Add(uid);
        }

        foreach (var uid in _linkConsoles)
        {
            if (_consoleQuery.TryComp(uid, out var console))
                UpdateUi((uid, console));
        }
    }

    #endregion

    #region Interface

    private BulkMiningLinkUiState GetLinkUiState(Entity<BulkAutoMiningConsoleComponent> console, List<EntityUid> ownLasers)
    {
        var now = _timing.CurTime;
        var state = new BulkMiningLinkUiState();
        var consoleXform = Transform(console);
        if (consoleXform.GridUid is not { } ownGrid)
            return state;

        _linkGridQuery.TryComp(ownGrid, out var own);
        CollectConsortium(ownGrid, _consortium);
        foreach (var member in _consortium)
        {
            state.Members.Add(new BulkMiningConsortiumMemberState(GetNetEntity(member), GetShipName(member)));
        }

        state.Bonus = GetConsortiumBonus(_consortium.Count);
        state.NextBonus = GetConsortiumBonus(_consortium.Count + 1);
        state.SpeedBonus = GetConsortiumSpeedBonus(_consortium.Count);
        state.NextSpeedBonus = GetConsortiumSpeedBonus(_consortium.Count + 1);
        state.FreeLasers = CountFreeLasers(ownGrid);

        var position = _transform.GetWorldPosition(consoleXform);
        for (var i = 0; i < ownLasers.Count; i++)
        {
            var laser = ownLasers[i];
            if (!_emitterQuery.TryComp(laser, out var comp) || comp.LinkPartner is not { } partner ||
                comp.LinkGrid is not { } partnerGrid || TerminatingOrDeleted(partner))
                continue;

            var distance = Vector2.Distance(_transform.GetWorldPosition(laser), _transform.GetWorldPosition(partner));
            state.Links.Add(new BulkMiningLinkState(GetNetEntity(partnerGrid), GetShipName(partnerGrid),
                GetNetEntity(laser), GetLaserName(laser, comp, i + 1), GetLaserName(partner, CompOrNull<BulkAutoMiningEmitterComponent>(partner), null),
                distance, comp.LinkRange, comp.LinkObstructedChecks > 0));
        }

        // Ships able to answer: a powered console within range, plus any ship with a pending request.
        _linkCandidates.Clear();
        var query = EntityQueryEnumerator<BulkAutoMiningConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var xform))
        {
            if (xform.GridUid is not { } grid || grid == ownGrid || xform.MapUid != consoleXform.MapUid ||
                ContainsCandidate(grid) || !IsPoweredAndAnchored(uid) ||
                !_gridQuery.TryComp(grid, out var gridComp) || !_xformQuery.TryComp(grid, out var gridXform))
                continue;

            var distance = GetDistanceToGrid(position, (grid, gridComp, gridXform));
            if (distance <= console.Comp.MaxRange)
                _linkCandidates.Add((grid, distance));
        }

        if (own != null)
        {
            AddRequestCandidates(own.Incoming.Keys, position, consoleXform.MapUid);
            AddRequestCandidates(own.Outgoing.Keys, position, consoleXform.MapUid);
        }

        _linkCandidates.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));
        var ownFree = state.FreeLasers > 0;
        for (var i = 0; i < _linkCandidates.Count && i < console.Comp.MaxLinkCandidates; i++)
        {
            var (grid, distance) = _linkCandidates[i];
            var status = BulkMiningLinkShipStatus.Available;
            var timeLeft = 0f;
            var free = CountFreeLasers(grid);
            if (AreLinked(ownGrid, grid))
            {
                status = BulkMiningLinkShipStatus.Linked;
            }
            else if (own != null && own.Incoming.TryGetValue(grid, out var incoming))
            {
                status = BulkMiningLinkShipStatus.IncomingRequest;
                timeLeft = (float)Math.Max(0, (incoming - now).TotalSeconds);
            }
            else if (own != null && own.Outgoing.TryGetValue(grid, out var outgoing))
            {
                status = BulkMiningLinkShipStatus.OutgoingRequest;
                timeLeft = (float)Math.Max(0, (outgoing - now).TotalSeconds);
            }
            else if (free == 0)
            {
                status = BulkMiningLinkShipStatus.NoFreeLaser;
            }
            else if (ownFree && TryGetLinkConsole(grid, out var other) &&
                     !TryFindLaserPairLineOfSight(ownGrid, grid, Math.Min(console.Comp.MaxRange, other.Comp.MaxRange)))
            {
                status = BulkMiningLinkShipStatus.Obstructed;
            }

            state.Ships.Add(new BulkMiningLinkShipState(GetNetEntity(grid), GetShipName(grid), distance, status, free, timeLeft));
        }

        return state;
    }

    private void AddRequestCandidates(IEnumerable<EntityUid> grids, Vector2 position, EntityUid? map)
    {
        foreach (var grid in grids)
        {
            if (ContainsCandidate(grid) || TerminatingOrDeleted(grid) || !_gridQuery.TryComp(grid, out var gridComp) ||
                !_xformQuery.TryComp(grid, out var gridXform) || gridXform.MapUid != map)
                continue;

            _linkCandidates.Add((grid, GetDistanceToGrid(position, (grid, gridComp, gridXform))));
        }
    }

    private bool ContainsCandidate(EntityUid grid)
    {
        foreach (var (candidate, _) in _linkCandidates)
        {
            if (candidate == grid)
                return true;
        }

        return false;
    }

    /// <summary>Only the nearest free pair is raycast, keeping the open console to one ray per listed ship.</summary>
    private bool TryFindLaserPairLineOfSight(EntityUid ownGrid, EntityUid otherGrid, float range)
    {
        _ownLasers.Clear();
        _otherLasers.Clear();
        GetFreeLasers(ownGrid, _ownLasers);
        GetFreeLasers(otherGrid, _otherLasers);
        var best = float.MaxValue;
        EntityUid bestOwn = default;
        EntityUid bestOther = default;
        foreach (var own in _ownLasers)
        {
            var ownPosition = _transform.GetWorldPosition(own);
            foreach (var other in _otherLasers)
            {
                var distance = Vector2.Distance(ownPosition, _transform.GetWorldPosition(other));
                if (distance > range || distance >= best)
                    continue;

                best = distance;
                bestOwn = own;
                bestOther = other;
            }
        }

        return best < float.MaxValue && IsLinkClear(bestOwn, bestOther);
    }

    private string GetLaserName(EntityUid uid, BulkAutoMiningEmitterComponent? comp, int? index)
    {
        var name = comp?.ConsoleName is { } shortName ? Loc.GetString(shortName) : Name(uid);
        return index is { } number
            ? Loc.GetString("bulk-auto-mining-laser-name", ("name", name), ("index", number))
            : name;
    }

    #endregion
}
