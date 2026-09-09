using Content.Server.Power.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.Components;
using Content.Shared.Research.Components;
using Content.Shared.Verbs;
using Content.Shared.Whitelist;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._Exodus.Research;

/// <summary>
/// Finds nearby entities allowed by <see cref="ResearchServerDiskMagnetComponent.Whitelist"/>
/// and lets their systems handle insertion into an R&amp;D server.
/// </summary>
public sealed class ResearchServerDiskMagnetSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<EntityUid> _nearbyEntities = new();

    private EntityQuery<ApcPowerReceiverComponent> _powerQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;

    private TimeSpan _nextScan = TimeSpan.MaxValue;

    public override void Initialize()
    {
        base.Initialize();

        _powerQuery = GetEntityQuery<ApcPowerReceiverComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();

        SubscribeLocalEvent<ResearchServerDiskMagnetComponent, MapInitEvent>(OnMagnetMapInit);
        SubscribeLocalEvent<ResearchServerDiskMagnetComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ResearchServerDiskMagnetComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
    }

    private void OnMagnetMapInit(Entity<ResearchServerDiskMagnetComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextScan = _timing.CurTime;

        if (ent.Comp.MagnetEnabled && ent.Comp.ScanInterval > TimeSpan.Zero)
            ScheduleScan(ent.Comp.NextScan);
    }

    private void OnGetVerbs(Entity<ResearchServerDiskMagnetComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !HasComp<HandsComponent>(args.User))
            return;

        args.Verbs.Add(new AlternativeVerb
        {
            Act = () => SetEnabled(ent, !ent.Comp.MagnetEnabled),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/Spare/poweronoff.svg.192dpi.png")),
            Text = Loc.GetString("magnet-pickup-component-toggle-verb"),
            Priority = 3,
        });
    }

    private void OnExamined(Entity<ResearchServerDiskMagnetComponent> ent, ref ExaminedEvent args)
    {
        var state = Loc.GetString(ent.Comp.MagnetEnabled
            ? "magnet-pickup-component-magnet-on"
            : "magnet-pickup-component-magnet-off");

        args.PushMarkup(Loc.GetString("magnet-pickup-component-on-examine-main", ("stateText", state)));
    }

    public void SetEnabled(Entity<ResearchServerDiskMagnetComponent> ent, bool enabled)
    {
        if (TerminatingOrDeleted(ent) || ent.Comp.MagnetEnabled == enabled)
            return;

        ent.Comp.MagnetEnabled = enabled;
        if (!enabled || ent.Comp.ScanInterval <= TimeSpan.Zero)
            return;

        ent.Comp.NextScan = _timing.CurTime;
        ScheduleScan(ent.Comp.NextScan);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var currentTime = _timing.CurTime;
        if (currentTime < _nextScan)
            return;

        var nextScan = TimeSpan.MaxValue;
        var query = EntityManager.AllEntityQueryEnumerator<ResearchServerDiskMagnetComponent,
            ResearchServerComponent,
            TransformComponent>();

        while (query.MoveNext(out var uid, out var magnet, out var server, out var xform))
        {
            if (TerminatingOrDeleted(uid) || !magnet.MagnetEnabled || magnet.ScanInterval <= TimeSpan.Zero)
                continue;

            if (magnet.NextScan > currentTime)
            {
                nextScan = Min(nextScan, magnet.NextScan);
                continue;
            }

            magnet.NextScan = currentTime + magnet.ScanInterval;
            nextScan = Min(nextScan, magnet.NextScan);

            if (IsPaused(uid) ||
                magnet.Range <= 0f ||
                magnet.MaxEntitiesPerScan <= 0 ||
                !HasRequiredPower((uid, magnet)))
            {
                continue;
            }

            ScanForEntities((uid, magnet, server, xform));
        }

        _nextScan = nextScan;
    }

    private void ScanForEntities(
        Entity<ResearchServerDiskMagnetComponent, ResearchServerComponent, TransformComponent> ent)
    {
        _nearbyEntities.Clear();
        _lookup.GetEntitiesInRange(ent.Comp3.MapID,
            _transform.GetWorldPosition(ent.Comp3),
            ent.Comp1.Range,
            _nearbyEntities,
            LookupFlags.Dynamic | LookupFlags.Sundries);

        var inserted = 0;
        var parent = ent.Comp3.ParentUid;

        foreach (var candidate in _nearbyEntities)
        {
            if (inserted >= ent.Comp1.MaxEntitiesPerScan)
                break;

            if (candidate == ent.Owner || candidate == parent || TerminatingOrDeleted(candidate) ||
                _whitelist.IsWhitelistFail(ent.Comp1.Whitelist, candidate))
            {
                continue;
            }

            if (ent.Comp1.OnlyOnGround &&
                (!_physicsQuery.TryComp(candidate, out var physics) || physics.BodyStatus != BodyStatus.OnGround))
            {
                continue;
            }

            var insertAttempt = new ResearchServerMagnetInsertAttemptEvent((ent.Owner, ent.Comp2));
            RaiseLocalEvent(candidate, ref insertAttempt);

            if (insertAttempt.Handled)
                inserted++;
        }
    }

    private bool HasRequiredPower(Entity<ResearchServerDiskMagnetComponent> ent)
    {
        return !ent.Comp.RequiresPower ||
               _powerQuery.TryComp(ent, out var power) && power.Powered;
    }

    private void ScheduleScan(TimeSpan scanTime)
    {
        _nextScan = Min(_nextScan, scanTime);
    }

    private static TimeSpan Min(TimeSpan first, TimeSpan second)
    {
        return first <= second ? first : second;
    }
}

/// <summary>
/// Raised on a nearby whitelisted entity so its system can insert it into the R&amp;D server.
/// </summary>
[ByRefEvent]
public record struct ResearchServerMagnetInsertAttemptEvent(Entity<ResearchServerComponent> Server)
{
    public bool Handled;
}
